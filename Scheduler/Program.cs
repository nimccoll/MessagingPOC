//===============================================================================
// Microsoft FastTrack for Azure
// Sphere Messaging POC
//===============================================================================
// Copyright © Microsoft Corporation.  All rights reserved.
// THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY
// OF ANY KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT
// LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS FOR A PARTICULAR PURPOSE.
//===============================================================================
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;

namespace Scheduler
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            HostBuilder builder = new HostBuilder();

            // Allows us to read the configuration file from current directory
            // (remember to copy those files to the OutputDirectory in VS)
            builder.UseContentRoot(Directory.GetCurrentDirectory());

            builder.ConfigureWebJobs(b =>
            {
                b.AddAzureStorageCoreServices();
                b.AddAzureStorageQueues();
            });

            // This step allows the environment variables to be read BEFORE the rest of the configuration
            // This is useful in configuring the hosting environment in debug, by setting the 
            // ENVIRONMENT variable in VS
            builder.ConfigureHostConfiguration(config =>
            {
                config.AddEnvironmentVariables();
            });

            // Read the configuration from json file
            builder.ConfigureAppConfiguration((context, config) =>
            {
                IHostingEnvironment env = context.HostingEnvironment;

                config
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);

                config.AddEnvironmentVariables();
            });

            // Configure logging (you can use the config here, via context.Configuration)
            builder.ConfigureLogging((context, loggingBuilder) =>
            {
                loggingBuilder.AddConfiguration(context.Configuration.GetSection("Logging"));
                loggingBuilder.AddConsole();

                // If this key exists in any config, use it to enable App Insights
                var appInsightsKey = context.Configuration["APPINSIGHTS_INSTRUMENTATIONKEY"];
                if (!string.IsNullOrEmpty(appInsightsKey))
                {
                    loggingBuilder.AddApplicationInsights(appInsightsKey);
                }
            });

            builder.UseConsoleLifetime();

            IHost host = builder.Build();
            using (host)
            {
                JobHost jobHost = host.Services.GetService(typeof(IJobHost)) as JobHost;

                await host.StartAsync();
                await jobHost.CallAsync("StartCollectors");
                await host.StopAsync();
            }
        }
    }
}
