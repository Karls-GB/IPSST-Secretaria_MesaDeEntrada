using IPSST.Application.Configuration;
using IPSST.Application.Services;
using IPSSTLoader.Application.Services;
using IPSSTLoader.Application.Workflows;
using IPSSTLoader.Domain.Interface;
using IPSSTLoader.Domain.Validation;
using IPSSTLoader.Infrastructure.Automation;
using IPSSTLoader.Infrastructure.Persistence;
using IPSSTLoader.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace IPSSTLoader
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

            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();

            using (var scope = ServiceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

        private void ConfigurarLogging()
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "log-.txt");

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
            var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: false).Build();
            bool headless = configuration.GetValue<bool>("PlaywrightSettings:Headless");
            string baseUrl = configuration.GetValue<string>("PlaywrightSettings:BaseUrl")!;
            string extentionUrl = configuration.GetValue<string>("PlaywrightSettings:ExtentionUrl")!;

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

            services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source = ipsstloader.db"));
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
