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
using Microsoft.Azure.WebJobs;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;

namespace SphereFunctions
{
    public class SharePointCollector
    {
        private List<string> _directories = new List<string>();
        private List<string> _files = new List<string>();

        [FunctionName("SharePointCollector")]
        public void Run([QueueTrigger("sharepointcollectorqueue", Connection = "AzureQueueStorage")]string myQueueItem, ILogger log)
        {
            log.LogInformation($"SharePointCollector function triggered by the following message: {myQueueItem}");
            dynamic collectorDetails = JsonConvert.DeserializeObject<dynamic>(myQueueItem);
            string accessToken = GetAccessToken(log);
            if (!string.IsNullOrEmpty(accessToken))
            {
                foreach (dynamic documentLibrary in collectorDetails.DocumentLibraries)
                {
                    log.LogInformation($"Scanning document library {documentLibrary.DocumentLibraryToScan}...");
                    string url = $"/v1.0/sites/{Environment.GetEnvironmentVariable("SharePointHost", EnvironmentVariableTarget.Process)}/drives";
                    dynamic siteData = QueryGraph(accessToken, url, log);
                    foreach (dynamic drive in siteData.value)
                    {
                        if (drive.name == documentLibrary.DocumentLibraryToScan)
                        {
                            url = $"/v1.0/sites/{Environment.GetEnvironmentVariable("SharePointHost", EnvironmentVariableTarget.Process)}/drives/{drive.id}/root/children";
                            GetFilesAndDirectores(accessToken, url, drive.id.ToString(), log);

                            Console.WriteLine($"Scanning of document library {documentLibrary.DocumentLibraryToScan} complete. Writing details to blob storage and notifying receiver.");
                            WriteCollectorDetailsToBlobStorage(Environment.GetEnvironmentVariable("AzureQueueStorage", EnvironmentVariableTarget.Process));
                        }
                    }
                }
            }
            log.LogInformation($"SharePointCollector processing of {myQueueItem} complete.");
        }

        private void GetFilesAndDirectores(string accessToken, string url, string driveId, ILogger log)
        {
            dynamic driveItems = QueryGraph(accessToken, url, log);
            foreach (dynamic fileOrFolder in driveItems.value)
            {
                dynamic file;
                try
                {
                    if (fileOrFolder.file != null)
                    {
                        file = fileOrFolder.file; // Will throw an exception if the file property does not exist
                        _files.Add(fileOrFolder.webUrl.ToString());
                        //FileOrFolder item = new FileOrFolder()
                        //{
                        //    Type = "file",
                        //    Id = fileOrFolder.id,
                        //    Name = fileOrFolder.name,
                        //    Url = fileOrFolder.webUrl,
                        //    ParentId = fileOrFolder.parentReference.id,
                        //    ParentPath = fileOrFolder.parentReference.path,
                        //    MimeType = fileOrFolder.file.mimeType
                        //};
                        //items.Add(item);
                    }
                    else
                    {
                        _directories.Add(fileOrFolder.webUrl.ToString());
                        string folderUrl = $"/v1.0/sites/{Environment.GetEnvironmentVariable("SharePointHost", EnvironmentVariableTarget.Process)}/drives/{driveId}/items/{fileOrFolder.id}/children";
                        GetFilesAndDirectores(accessToken, folderUrl, driveId, log);
                        //FileOrFolder item = new FileOrFolder()
                        //{
                        //    Type = "folder",
                        //    Id = fileOrFolder.id,
                        //    Name = fileOrFolder.name,
                        //    Url = fileOrFolder.webUrl,
                        //    ParentId = fileOrFolder.parentReference.id,
                        //    ParentPath = fileOrFolder.parentReference.path
                        //};
                        //items.Add(item);
                    }
                }
                catch (RuntimeBinderException)
                {
                    _directories.Add(fileOrFolder.webUrl.ToString());
                    //string folderUrl = $"/v1.0/sites/{Environment.GetEnvironmentVariable("SharePointHost", EnvironmentVariableTarget.Process)}/drives/{driveId}/items/{fileOrFolder.id}/children";
                    //GetFilesAndDirectores(accessToken, folderUrl, driveId, log);
                    //FileOrFolder item = new FileOrFolder()
                    //{
                    //    Type = "folder",
                    //    Id = fileOrFolder.id,
                    //    Name = fileOrFolder.name,
                    //    Url = fileOrFolder.webUrl,
                    //    ParentId = fileOrFolder.parentReference.id,
                    //    ParentPath = fileOrFolder.parentReference.path
                    //};
                    //items.Add(item);
                }
            }
        }

        private void WriteCollectorDetailsToBlobStorage(string storageConnectionString)
        {
            BlobServiceClient blobServiceClient = new BlobServiceClient(storageConnectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("sharepointcollector");
            if (!containerClient.Exists())
            {
                containerClient.Create(PublicAccessType.None);
            }

            StringBuilder sbCollectionDetails = new StringBuilder();
            sbCollectionDetails.Append("{\"CollectionDetails\": {\"RunDateTime\": \"");
            sbCollectionDetails.Append(DateTime.Now);
            sbCollectionDetails.Append("\", \"Directories\": [");
            foreach (string directory in _directories)
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
                queueClient.SendMessage($"sharepointcollector,{blobName}");
            }
        }

        private dynamic QueryGraph(string accessToken, string url, ILogger log)
        {
            string graphResourceId = Environment.GetEnvironmentVariable("ida:GraphResourceId", EnvironmentVariableTarget.Process);
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(graphResourceId);
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            HttpResponseMessage graphResult = httpClient.GetAsync(url).Result;
            string graphResultString = graphResult.Content.ReadAsStringAsync().Result;
            dynamic graphData = JsonConvert.DeserializeObject(graphResultString);

            return graphData;
        }

        private string GetAccessToken(ILogger log)
        {
            IConfidentialClientApplication app;
            string clientId = Environment.GetEnvironmentVariable("ida:ClientId", EnvironmentVariableTarget.Process);
            string clientSecret = Environment.GetEnvironmentVariable("ida:ClientSecret", EnvironmentVariableTarget.Process);
            string authority = $"{Environment.GetEnvironmentVariable("ida:Authority")}/{Environment.GetEnvironmentVariable("ida:Tenant", EnvironmentVariableTarget.Process)}";
            string accessToken = string.Empty;

            app = ConfidentialClientApplicationBuilder.Create(clientId)
                    .WithClientSecret(clientSecret)
                    .WithAuthority(new Uri(authority))
                    .Build();

            AuthenticationResult result = null;
            string[] scopes = new string[] { $"{Environment.GetEnvironmentVariable("ida:GraphResourceId", EnvironmentVariableTarget.Process)}/.default" };
            try
            {
                result = app.AcquireTokenForClient(scopes).ExecuteAsync().Result;
                accessToken = result.AccessToken;

                log.LogInformation("Token acquired");
            }
            catch (MsalServiceException ex) when (ex.Message.Contains("AADSTS70011"))
            {
                // Invalid scope. The scope has to be of the form "https://resourceurl/.default"
                // Mitigation: change the scope to be as expected
                log.LogError("Scope provided is not supported");
            }

            return accessToken;
        }
    }
}
