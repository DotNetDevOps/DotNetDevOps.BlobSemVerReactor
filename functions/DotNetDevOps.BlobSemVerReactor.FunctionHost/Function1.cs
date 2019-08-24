using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.Management.AppService.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using Newtonsoft.Json.Linq;

namespace DotNetDevOps.BlobSemVerReactor.FunctionHost
{

    /// <summary>
    // {
    //  "topic": "/subscriptions/0fd7128b-5305-49da-a400-b7a37feb271c/resourceGroups/io-board/providers/Microsoft.Storage/storageAccounts/ioboard",
    //  "subject": "/blobServices/default/containers/functions/blobs/com.io-board.forms.service-provider-host/1.0.0-ci-20190823-01/com.io-board.forms.service-provider-host-dummy.zip",
    //  "eventType": "Microsoft.Storage.BlobCreated",
    //  "eventTime": "2019-08-23T09:28:41.3448513Z",
    //  "id": "7fbdf40e-d01e-0030-4895-5903ee0686cf",
    //  "data": {
    //    "api": "PutBlob",
    //    "clientRequestId": "9676b7b0-a1d1-4c97-67fb-706c9c4c4e2b",
    //    "requestId": "7fbdf40e-d01e-0030-4895-5903ee000000",
    //    "eTag": "0x8D727AC4946A225",
    //    "contentType": "application/x-zip-compressed",
    //    "contentLength": 3670713,
    //    "blobType": "BlockBlob",
    //    "url": "https://ioboard.blob.core.windows.net/functions/com.io-board.forms.service-provider-host/1.0.0-ci-20190823-01/com.io-board.forms.service-provider-host-dummy.zip",
    //    "sequencer": "00000000000000000000000000002FF000000000043a0bdb",
    //    "storageDiagnostics": {
    //      "batchId": "26965b5d-f006-00c2-0095-59d17a000000"
    //    }
    //  },
    //  "dataVersion": "",
    //  "metadataVersion": "1"
    //}
    /// </summary>
    public class Function1
    {
        public IConfiguration configuration;
        public Function1(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        private static async Task<WebSiteManagementClient> CreateWebSiteManagementClientAsync(string subscriptionId)
        {
            var tokenProvider = new AzureServiceTokenProvider();

            var accessToken = await tokenProvider.GetAccessTokenAsync("https://management.azure.com/");
            var tokenCredentials = new TokenCredentials(accessToken);
            var azureCredentials = new AzureCredentials(
           tokenCredentials,
           tokenCredentials,
           "common",
           AzureEnvironment.AzureGlobalCloud);

            var client = RestClient
            .Configure()
            .WithEnvironment(AzureEnvironment.AzureGlobalCloud)
            .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
            .WithCredentials(azureCredentials)
            .Build();

            var websiteClient = new WebSiteManagementClient(client)
            {
                SubscriptionId = subscriptionId
            };

            return websiteClient;
        }



        [FunctionName("Function1")]
        public async Task Run([QueueTrigger("%queuename%")]string myQueueItem, ILogger log)
        {
            //Take the example json above and apply the updated version to appsetting WEBSITE_RUN_FROM_ZIP
            var json = JToken.Parse(myQueueItem);
            var eventType = json.SelectToken("$.eventType")?.ToString() ;
            if (eventType == "Microsoft.Storage.BlobCreated")
            {
                var url = json.SelectToken("$.data.url")?.ToString();
                var semVer = Regex.Match(url, configuration.GetValue<string>("FindSemVerRegex")).Groups[1].Value;
                Console.WriteLine(semVer);
                var wepapp = configuration.GetValue<string>("FunctionAppId");
                var subscription = wepapp.Trim('/').Split("/").Skip(1).First();
                var resourceGroupName = wepapp.Trim('/').Split("/").Skip(3).First();
                var siteName = wepapp.Trim('/').Split("/").Skip(7).First();
                var websiteClient = await CreateWebSiteManagementClientAsync(subscription);

                var site = await websiteClient.WebApps.GetAsync(resourceGroupName, siteName);

                var settings = await websiteClient.WebApps.ListApplicationSettingsAsync(resourceGroupName, siteName);
                if (settings.Properties.ContainsKey("WEBSITE_RUN_FROM_ZIP"))
                {
                    var currentSetting = settings.Properties["WEBSITE_RUN_FROM_ZIP"];
                    var currentSemVer = Regex.Match(currentSetting, configuration.GetValue<string>("FindSemVerRegex")).Groups[1].Value;
                    Console.WriteLine(currentSemVer);
                    if (currentSemVer == semVer)
                        return;

                    var s = SemanticVersion.Parse(currentSemVer);
                    var s1 = SemanticVersion.Parse(semVer);
                    if(s1 > s)
                    {
                        settings.Properties["WEBSITE_RUN_FROM_ZIP"] = url;
                        await websiteClient.WebApps.UpdateApplicationSettingsAsync(resourceGroupName, siteName, settings);
                    }

                }


            }

        }
    }
}
