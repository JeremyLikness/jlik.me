using System.Linq;
using System.Net;
using System.Net.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.ApplicationInsights;
using System;
using System.Threading.Tasks;

namespace jlikme
{
    public static class FunctionHost
    {
        public static TelemetryClient telemetry = new TelemetryClient()
        {
            InstrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")
        };

        public const string FALLBACK_URL = "https://blog.jeremylikness.com/?utm_source=jeliknes&utm_medium=redirect&utm_campaign=jlik_me";
        public const string KEEP_ALIVE = "xxxxxx";
        public const string KEEP_ALIVE_URL = "https://jlikme.azurewebsites.net/api/UrlRedirect/xxxxxx";

        [FunctionName(name: "UrlRedirect")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", 
            Route = "UrlRedirect/{shortUrl}")]HttpRequestMessage req, 
            [Table(tableName: "urls")]CloudTable inputTable, 
            string shortUrl,
            [Queue(queueName: "requests")]IAsyncCollector<string> queue,
            TraceWriter log)
        {
            log.Info($"C# HTTP trigger function processed a request for shortUrl {shortUrl}");

            if (shortUrl == KEEP_ALIVE)
            {
                log.Info("Exiting keep alive call.");
                var noContent = req.CreateResponse(HttpStatusCode.NoContent);
                return noContent;
            }

            var redirectUrl = FALLBACK_URL;
            
            if (!String.IsNullOrWhiteSpace(shortUrl))
            {
                shortUrl = shortUrl.Trim().ToLower();

                var partitionKey = $"{shortUrl.First()}";

                log.Info($"Searching for partition key {partitionKey} and row {shortUrl}.");

                var startTime = DateTime.UtcNow;
                var timer = System.Diagnostics.Stopwatch.StartNew();

                TableOperation operation = TableOperation.Retrieve<ShortUrl>(partitionKey, shortUrl);
                TableResult result = inputTable.Execute(operation);

                telemetry.TrackDependency("AzureTableStorage", "Retrieve", startTime, timer.Elapsed, result.Result != null);

                ShortUrl fullUrl = result.Result as ShortUrl;
                if (fullUrl != null)
                {
                    log.Info($"Found it: {fullUrl.Url}");
                    redirectUrl = WebUtility.UrlDecode(fullUrl.Url);
                    telemetry.TrackPageView(redirectUrl);
                    if (!string.IsNullOrWhiteSpace(fullUrl.Medium))
                    {
                        telemetry.TrackEvent(fullUrl.Medium);
                    }
                }
            }
            else
            {
                telemetry.TrackEvent("Bad Link");
            }

            await queue.AddAsync($"{shortUrl}|{redirectUrl}|{DateTime.UtcNow}");

            var res = req.CreateResponse(HttpStatusCode.Redirect);
            res.Headers.Add("Location", redirectUrl);
            return res;
        }

        [FunctionName("KeepAlive")]
        public static async Task KeepAlive([TimerTrigger(scheduleExpression: "0 */4 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info("Keep-Alive invoked.");
            var client = new HttpClient();
            await client.GetAsync(KEEP_ALIVE_URL);
        }
    }
}