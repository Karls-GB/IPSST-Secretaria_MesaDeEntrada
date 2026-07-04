using IPSSTLoader.Application.Services;
using IPSSTLoader.Application.Workflows;
using IPSSTLoader.Domain.Interface;
using IPSSTLoader.Domain.Validation;
using IPSSTLoader.Infrastructure.Automation;
using IPSSTLoader.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using System.Configuration;
using System.Data;
using System.Windows;

namespace IPSSTLoader
{

    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();

            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            //Dominio
            services.AddSingleton<ExpValidation>();

            //Infraestructura
            services.AddSingleton<PlaywrightSession>();
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

            //UI
            services.AddSingleton<MainWindow>();
        }
    }
}
