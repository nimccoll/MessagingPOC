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
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Agent
{
    internal class Program
    {
        private static bool _stop = false;

        static void Main(string[] args)
        {
            var environmentName = Environment.GetEnvironmentVariable("ENVIRONMENT");

            // Setup console application to read settings from appsettings.json
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, true)
                .AddJsonFile($"appsettings.{environmentName}.json", true, true);

            IConfigurationRoot configuration = configurationBuilder.Build();

            // Configure default dependency injection container
            ServiceProvider serviceProvider = new ServiceCollection()
                .AddLogging(configure => configure.AddConsole())
                .AddSingleton<IConfigurationRoot>(configuration)
                .BuildServiceProvider();

            ILogger logger = serviceProvider.GetService<ILogger<Program>>();

            logger.LogInformation("*** Queue Processor has started ***");
            logger.LogInformation("Retrieving messages from queue {0}", configuration.GetValue<string>("QueueName"));
            Task.Run(() =>
            {
                QueueClient queueClient = new QueueClient(configuration.GetValue<string>("AzureQueueStorage"), configuration.GetValue<string>("QueueName"));

                if (queueClient.Exists())
                {
                    do
                    {
                        QueueMessage[] messages = queueClient.ReceiveMessages();
                        if (messages != null && messages.Length > 0)
                        {
                            logger.LogInformation("Messages found. Executing collectors...");
                            foreach (QueueMessage message in messages)
                            {
                                byte[] decodedMessage = Convert.FromBase64String(message.MessageText);
                                dynamic collectorDetails = JsonConvert.DeserializeObject<dynamic>(Encoding.UTF8.GetString(decodedMessage));
                                foreach (dynamic collectorDetail in collectorDetails.RunCollectors)
                                {
                                    if (collectorDetail.CollectorName == "FileCollector")
                                    {
                                        logger.LogInformation("Executing FileCollector...");

                                        string fileCollectorPath = configuration.GetValue<string>("FileCollectorPath");
                                        string fileCollectorExecutable = $"{fileCollectorPath}{configuration.GetValue<string>("FileCollectorName")}";
                                        ProcessStartInfo processStartInfo = new ProcessStartInfo(fileCollectorExecutable, collectorDetail.Path.ToString());
                                        processStartInfo.WorkingDirectory = fileCollectorPath;
                                        System.Diagnostics.Process.Start(processStartInfo);
                                    }
                                }
                                queueClient.DeleteMessage(message.MessageId, message.PopReceipt);
                            }
                        }
                        if (_stop) break;
                        Task.Delay(3000).Wait();
                    } while (true);
                }
            });
            logger.LogInformation("*** Queue Processor is Running. Press any key to stop. ***");
            Console.Read();
            logger.LogInformation("*** Queue Processor is Stopping ***");
            _stop = true;
        }
    }
}
