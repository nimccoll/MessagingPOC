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
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileCollector
{
    internal class Program
    {
        private static List<string> _directories = new List<string>();
        private static List<string> _files = new List<string>();

        static void Main(string[] args)
        {
            var environmentName = Environment.GetEnvironmentVariable("ENVIRONMENT");

            // Setup console application to read settings from appsettings.json
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, true)
                .AddJsonFile($"appsettings.{environmentName}.json", true, true);

            IConfigurationRoot configuration = configurationBuilder.Build();

            if (args.Length == 1)
            {
                string pathToScan = args[0];
                Console.WriteLine($"File Collector is starting. Scanning path {pathToScan}...");
                if (!string.IsNullOrEmpty(pathToScan)
                    && Directory.Exists(pathToScan))
                {
                    GetFilesAndDirectories(pathToScan);

                    Console.WriteLine($"Scanning of path {pathToScan} complete. Writing details to blob storage and notifying receiver.");
                    string storageConnectionString = configuration.GetConnectionString("AzureStorageConnectionString");
                    WriteCollectorDetailsToBlobStorage(storageConnectionString);

                }
                Console.WriteLine($"File Collector complete. Found {_directories.Count} directories and {_files.Count} files.");
            }
        }

        private static void GetFilesAndDirectories(string path)
        {
            string[] directories = Directory.GetDirectories(path);
            string[] files = Directory.GetFiles(path);
            _directories.AddRange(directories);
            _files.AddRange(files);
            if (directories.Length > 0)
            {
                foreach (string dir in directories)
                {
                    GetFilesAndDirectories(dir);
                }
            }
        }

        private static void WriteCollectorDetailsToBlobStorage(string storageConnectionString)
        {
            BlobServiceClient blobServiceClient = new BlobServiceClient(storageConnectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("filecollector");
            if (!containerClient.Exists())
            {
                containerClient.Create(PublicAccessType.None);
            }

            StringBuilder sbCollectionDetails = new StringBuilder();
            sbCollectionDetails.Append("{\"CollectionDetails\": {\"RunDateTime\": \"");
            sbCollectionDetails.Append(DateTime.Now);
            sbCollectionDetails.Append("\", \"Directories\": [");
            foreach(string directory in _directories)
            {
                sbCollectionDetails.Append("{\"DirectoryName\": ");
                sbCollectionDetails.Append(JsonConvert.ToString(directory));
                sbCollectionDetails.Append("},");
            }
            sbCollectionDetails.Append("], \"Files\": [");
            foreach (string file in _files)
            {
                sbCollectionDetails.Append("{\"FileName\": ");
                sbCollectionDetails.Append(JsonConvert.ToString(file));
                sbCollectionDetails.Append("},");
            }
            sbCollectionDetails.Append("]}}");
            string collectionDetails = sbCollectionDetails.ToString();
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(collectionDetails);
            writer.Flush();
            stream.Position = 0;
            string blobName = $"collectiondetails{DateTime.Now.ToString("s")}.json".Replace(":", "");
            containerClient.UploadBlob(blobName, stream);

            QueueClient queueClient = new QueueClient(storageConnectionString, "receiverqueue", new QueueClientOptions() { MessageEncoding = QueueMessageEncoding.Base64 });

            if (queueClient.Exists())
            {
                queueClient.SendMessage($"filecollector,{blobName}");
            }
        }
    }
}
