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
using Microsoft.Extensions.Logging;

namespace Scheduler
{
    public class JobFunctions
    {
        private IConfiguration _configuration;

        public JobFunctions(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [NoAutomaticTrigger]
        public void StartCollectors(ILogger logger, [Queue("onpremisescollectorqueue", Connection = "AzureQueueStorage")] out string onpremisesMessage,
            [Queue("sharepointcollectorqueue", Connection = "AzureQueueStorage")] out string sharePointMessage)
        {
            string fileCollectorPathToScan = _configuration.GetValue<string>("FileCollectorPathToScan");
            string documentLibaryToScan = _configuration.GetValue<string>("DocumentLibraryToScan");

            logger.LogInformation("Function StartCollectors begins...");

            onpremisesMessage = "{\"RunCollectors\": [ {\"CollectorName\": \"FileCollector\", \"Path\": \"" + fileCollectorPathToScan + "\"} ]}";
            sharePointMessage = "{\"DocumentLibraries\": [ {\"DocumentLibraryToScan\": \"" + documentLibaryToScan +"\"} ]}";
            
            logger.LogInformation("Function StartCollectors ends...");
        }
    }
}
