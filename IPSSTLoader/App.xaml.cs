using IPSST.Application.Configuration;
using IPSST.Application.Services;
using IPSST.Application.Workflows;
using IPSST.Domain.Interface;
using IPSST.Domain.Validation;
using IPSST.Infrastructure.Automation;
using IPSST.Infrastructure.Persistence;
using IPSST.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace IPSST
{

    public partial class App : System.Windows.Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ConfigurarLogging();
            ConfigurarManejadorDeErroresGlobales();

            Log.Information("Iniciando aplicacion");

            try
            {
                await IniciarAsync();
            }
            catch (Exception ex)
            {
                // Sin esto la app queda abierta sin ventanas (ShutdownMode = OnExplicitShutdown)
                Log.Fatal(ex, "Error al iniciar la aplicacion");
                MessageBox.Show($"No se pudo iniciar la aplicacion: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private async Task IniciarAsync()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();

            using (var context = ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
            {
                context.Database.EnsureCreated();
            }

            var session = ServiceProvider.GetRequiredService<PlaywrightSession>();
            await session.InitializeAsync();

            bool loggedIn = false;
            string loginWindowUsuario = string.Empty;
            while (!loggedIn)
            {
                var loginWindow = new LoginWindow();
                loginWindow.ShowDialog();

                if (!loginWindow.LoginConfirmed)
                {
                    Log.Information("Usuario cerro la ventana de login sin ingresar. Cerrando la aplicacion");
                    Shutdown();
                    return;
                }

                loggedIn = await session.LoginAsync(loginWindow.Username, loginWindow.Password);

                if (!loggedIn)
                {
                    Log.Warning("Intento de login fallido para el usuario {Username}", loginWindow.Username);
                    MessageBox.Show("Usuario o contraseña incorrectos. Intente nuevamente");
                }

                loginWindowUsuario = loginWindow.Username;
            }

            Log.Information("Usuario {Username} logueado exitosamente", loginWindowUsuario);

            var oficinaCacheService = ServiceProvider.GetRequiredService<OficinaCacheService>();
            await oficinaCacheService.InitializeAsync();
            Log.Information("Oficinas cargadas: {Cantidad}", oficinaCacheService.Oficinas.Count);

            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            this.MainWindow = mainWindow;
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

        }

        private static string ObtenerCarpetaLogs()
        {
            var usuario = string.Concat(Environment.UserName.Split(Path.GetInvalidFileNameChars()));
            var carpeta = Path.Combine(AppContext.BaseDirectory, "logs", usuario);

            try
            {
                Directory.CreateDirectory(carpeta);
                return carpeta;
            }
            catch (Exception)
            {
                // Sin permiso de escritura en la carpeta compartida: se loguea en la PC del usuario
                var local = Path.Combine(ObtenerCarpetaLocal(), "logs");
                Directory.CreateDirectory(local);
                return local;
            }
        }

        // Datos propios de cada usuario en su PC (sin permisos de administrador)
        private static string ObtenerCarpetaLocal()
        {
            var carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPSST");
            Directory.CreateDirectory(carpeta);
            return carpeta;
        }

        private void ConfigurarLogging()
        {
            // Una carpeta por usuario junto al exe: varios usuarios usan el programa desde la carpeta
            // compartida a la vez, y asi no escriben sobre el mismo archivo
            var logPath = Path.Combine(ObtenerCarpetaLogs(), "log-.txt");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(logPath,
                    rollingInterval: RollingInterval.Day, 
                    retainedFileCountLimit: 14, 
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        private void ConfigurarManejadorDeErroresGlobales()
        {
            //Error no manejado en hilos de UI
            DispatcherUnhandledException += (s, args) =>
            {
                Log.Fatal(args.Exception, "Excepción no controlada en el hilo de la interfaz de usuario");
                MessageBox.Show("Ocurrió un error inesperado, Revise el archivo de log para mas detalles.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            //Error no manejado fuera del hilo de UI
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    Log.Fatal(args.ExceptionObject as Exception, "Excepción no manejada fuera del hilo de la UI");
                    Log.CloseAndFlush();
                }
            };

            //Excepciones de tareas asincrónicas no esperadas
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Log.Error(args.Exception, "Excepción no observada en una tarea asincrónica");
                args.SetObserved();
            };
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Dos capas: la configuracion comun (carpeta compartida) y la personal de cada usuario.
            // La personal es opcional y solo pisa las claves que define; el resto sale de la comun
            var configUsuario = Path.Combine(ObtenerCarpetaLocal(), "appsettings.usuario.json");
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile(configUsuario, optional: true)
                .Build();

            if (File.Exists(configUsuario))
            {
                Log.Information("Usando configuracion personal de {Ruta}", configUsuario);
            }
            bool headless = configuration.GetValue<bool>("PlaywrightSettings:Headless");
            string baseUrl = configuration.GetValue<string>("PlaywrightSettings:BaseUrl")!;
            string extentionUrl = configuration.GetValue<string>("PlaywrightSettings:ExtentionUrl")!;

            var oficinaConfig = configuration.GetSection("Oficina").Get<OficinaConfig>();
            if (string.IsNullOrWhiteSpace(oficinaConfig?.Nombre))
            {
                throw new InvalidOperationException($"Falta 'Oficina:Nombre' en appsettings.json (o esta vacio en {configUsuario})");
            }
            services.AddSingleton(oficinaConfig);

            var paseDefaults = configuration.GetSection("PaseDefaults").Get<Dictionary<string, PaseDefaultConfig>>()
                ?? new Dictionary<string, PaseDefaultConfig>();
            services.AddSingleton(paseDefaults);

            var resolucionDefaults = configuration.GetSection("ResolucionDefaults").Get<Dictionary<string, ResolucionDefaultConfig>>()
                ?? new Dictionary<string, ResolucionDefaultConfig>();
            services.AddSingleton(resolucionDefaults);

            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(dispose: false);
            });

            //Dominio
            services.AddSingleton<ExpValidation>();

            //Infraestructura
            services.AddSingleton<PlaywrightSession>(sp =>
                new PlaywrightSession(headless, baseUrl, sp.GetRequiredService<ILogger<PlaywrightSession>>(), extentionUrl));

            // La base va en la PC de cada usuario: SQLite no es confiable en carpetas de red
            // y varios usuarios escribiendo a la vez la pueden bloquear o danar
            var dbPath = Path.Combine(ObtenerCarpetaLocal(), "ipsstloader.db");
            services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
            services.AddScoped<IUploadJobRepository, UploadJobRepository>();
            services.AddScoped<IAutomationBusqueda, PlaywrightBusqueda>();
            services.AddScoped<IAutomationPase, PlaywrightPase>();
            services.AddScoped<IAutomationRecepcion, PlaywrightRecepcion>();
            services.AddScoped<IAutomationResolucion, PlaywrightResolucion>();

            //Aplicacion
            services.AddScoped<BusquedaService>();
            services.AddScoped<RecepcionService>();
            services.AddScoped<PaseWorkflow>();
            services.AddScoped<ResolucionWorkflow>();
            services.AddScoped<CancelarPaseWorkflow>();
            services.AddSingleton<OficinaCacheService>();

            //UI
            services.AddSingleton<MainWindow>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Cerrando aplicacion");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
