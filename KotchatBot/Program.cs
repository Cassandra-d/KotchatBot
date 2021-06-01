using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using KotchatBot.Configuration;

namespace KotchatBot
{
    class Program
    {
        private static IConfiguration _configuration;
        private static Core.Manager _manager;

        static async Task Main(string[] args)
        {
            using (IHost host = CreateHostBuilder(args).Build())
            {
                var folderDataSourceOptions = new FolderDataSourceOptions();
                var imgurOptions = new ImgurDataSourceOptions();
                var generalOptions = new GeneralOptions();
                _configuration.GetSection(nameof(FolderDataSourceOptions)).Bind(folderDataSourceOptions);
                _configuration.GetSection(nameof(ImgurDataSourceOptions)).Bind(imgurOptions);
                _configuration.GetSection(nameof(GeneralOptions)).Bind(generalOptions);

                var containerBuilder = new ContainerBuilder();
                IoC.Utils.RegisterDependencies(containerBuilder);
                containerBuilder.RegisterInstance(folderDataSourceOptions);
                containerBuilder.RegisterInstance(imgurOptions);
                containerBuilder.RegisterInstance(generalOptions);
                var container = containerBuilder.Build();

                var lifetime = host.Services.GetService(typeof(IHostLifetime));

                _manager = container.Resolve<KotchatBot.Core.Manager>();

                await host.RunAsync();

                _manager.Stop();
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for proper cancellation of everything
            }
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, configuration) =>
                {
                    configuration.Sources.Clear();

                    IHostEnvironment env = hostingContext.HostingEnvironment;

                    configuration
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, true);

                    IConfigurationRoot configurationRoot = configuration.Build();
                    _configuration = configurationRoot;
                });
    }
}
