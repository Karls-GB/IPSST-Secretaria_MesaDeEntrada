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
using System.Configuration;
using System.Data;
using System.Windows;

namespace IPSSTLoader
{

    public partial class App : System.Windows.Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
            while (!loggedIn)
            {
                var loginWindow = new LoginWindow();
                loginWindow.ShowDialog();

                if (!loginWindow.LoginConfirmed)
                {
                    Shutdown();
                    return;
                }

                loggedIn = await session.LoginAsync(loginWindow.Username, loginWindow.Password);

                if (!loggedIn)
                {
                    MessageBox.Show("Usuario o contraseña incorrectos. Intente nuevamente");
                }
            }

            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: false).Build();
            bool headless = configuration.GetValue<bool>("PlaywrightSettings:Headless");

            //Dominio
            services.AddSingleton<ExpValidation>();

            //Infraestructura
            services.AddSingleton(new PlaywrightSession(headless));
            services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source = ipsstlaoder.db"));
            services.AddScoped<IUploadJobRepository, UploadJobRepository>();
            services.AddScoped<IAutomationBusqueda, PlaywrightBusqueda>();
            services.AddScoped<IAutomationPase, PlaywrightPase>();
            //services.AddScoped<IAutomationRecepcion, PlaywrightRecepcion>();
            //services.AddScoped<IAutomationResolucion, PlaywrightResolucion>();

            //Aplicacion
            services.AddScoped<BusquedaService>();
            services.AddScoped<RecepcionService>();
            services.AddScoped<PaseWorkflow>();
            services.AddScoped<ResolucionWorkflow>();

            //UI
            services.AddSingleton<MainWindow>();
        }
    }
}
