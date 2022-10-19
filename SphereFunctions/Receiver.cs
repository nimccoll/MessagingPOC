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
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using static System.Reflection.Metadata.BlobBuilder;

namespace SphereFunctions
{
    public class Receiver
    {
        [FunctionName("Receiver")]
        public void Run([QueueTrigger("receiverqueue", Connection = "AzureQueueStorage")] string myQueueItem, ILogger log)
        {
            log.LogInformation($"Receiver function triggered by the following message: {myQueueItem}");
            string[] collectionDataLocation = myQueueItem.Split(',');

            BlobServiceClient blobServiceClient = new BlobServiceClient(Environment.GetEnvironmentVariable("AzureQueueStorage", EnvironmentVariableTarget.Process));
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(collectionDataLocation[0]);
            BlobClient collectionBlob = containerClient.GetBlobClient(collectionDataLocation[1]);

            if (collectionBlob.Exists())
            {
                log.LogInformation($"Downloading collection details from {collectionBlob.Name} ...");
                BlobDownloadInfo download = collectionBlob.Download();
                string collectionData = string.Empty;
                using (StreamReader reader = new StreamReader(download.Content))
                {
                    collectionData = reader.ReadToEnd();
                }

                log.LogInformation("Writing collection details to table rawcollectiondata...");
                CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("AzureQueueStorage", EnvironmentVariableTarget.Process));
                CloudTableClient cloudTableClient = cloudStorageAccount.CreateCloudTableClient(new TableClientConfiguration());
                CloudTable rawCollectionDataTable = cloudTableClient.GetTableReference("rawcollectiondata");
                bool exists = rawCollectionDataTable.Exists();

                if (exists)
                {
                    dynamic collectorResults = JsonConvert.DeserializeObject<dynamic>(collectionData);

                    int i = 1;
                    foreach (dynamic directory in collectorResults.CollectionDetails.Directories)
                    {
                        DynamicTableEntity dynamicTableEntity = new DynamicTableEntity();
                        dynamicTableEntity.PartitionKey = collectionDataLocation[1];
                        dynamicTableEntity.RowKey = $"Directory{i}";
                        dynamicTableEntity.Properties.Add("Type", EntityProperty.CreateEntityPropertyFromObject("Directory"));
                        dynamicTableEntity.Properties.Add("Name", EntityProperty.CreateEntityPropertyFromObject(directory.DirectoryName));
                        TableOperation createOperation = TableOperation.Insert(dynamicTableEntity);
                        TableResult result = rawCollectionDataTable.Execute(createOperation);
                        i++;
                    }
                    int j = 1;
                    foreach (dynamic file in collectorResults.CollectionDetails.Files)
                    {
                        DynamicTableEntity dynamicTableEntity = new DynamicTableEntity();
                        dynamicTableEntity.PartitionKey = collectionDataLocation[1];
                        dynamicTableEntity.RowKey = $"File{j}";
                        dynamicTableEntity.Properties.Add("Type", EntityProperty.CreateEntityPropertyFromObject("File"));
                        dynamicTableEntity.Properties.Add("Name", EntityProperty.CreateEntityPropertyFromObject(file.FileName));
                        TableOperation createOperation = TableOperation.Insert(dynamicTableEntity);
                        TableResult result = rawCollectionDataTable.Execute(createOperation);
                        j++;
                    }
                }
                log.LogInformation($"Receiver processing of {collectionBlob.Name} complete.");
            }
        }
    }
}
