using System.Linq;
using System.Net;
using System.Net.Http;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.ApplicationInsights;
using System;
using System.Web;
using System.Threading.Tasks;
using System.Dynamic;
using System.Collections.Generic;

namespace jlikme
{
    public static class FunctionHost
    {
        public static TelemetryClient telemetry = new TelemetryClient()
        {
            InstrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")
        };

        public const string ROBOTS = "robots.txt";
        public const string ROBOT_RESPONSE = "User-agent: *\nDisallow: /";
        public const string FALLBACK_URL = "https://blog.jeremylikness.com/?utm_source=jeliknes&utm_medium=redirect&utm_campaign=jlik_me";
        public const string KEEP_ALIVE = "xxxxxx";
        public const string KEEP_ALIVE_URL = "https://jlikme.azurewebsites.net/api/UrlRedirect/xxxxxx";
        public const string URL_TRACKING = "url-tracking";
        public const string URL_STATS = "url-stats";

        [FunctionName(name: "UrlRedirect")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", 
            Route = "UrlRedirect/{shortUrl}")]HttpRequestMessage req, 
            [Table(tableName: "urls")]CloudTable inputTable, 
            string shortUrl,
            [Queue(queueName: "requests")]IAsyncCollector<string> queue,
            TraceWriter log)
        {
            log.Info($"C# HTTP trigger function processed a request for shortUrl {shortUrl}");

            shortUrl = shortUrl.ToLower();

            if (shortUrl == KEEP_ALIVE)
            {
                log.Info("Exiting keep alive call.");
                var noContent = req.CreateResponse(HttpStatusCode.NoContent);
                return noContent;
            }

            if (shortUrl == ROBOTS)
            {
                log.Info("Request for robots.txt.");
                var robotResponse = req.CreateResponse(HttpStatusCode.OK, ROBOT_RESPONSE, "text/plain");
                return robotResponse;
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

                if (result.Result is ShortUrl fullUrl)
                {
                    log.Info($"Found it: {fullUrl.Url}");
                    redirectUrl = WebUtility.UrlDecode(fullUrl.Url);
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

        [FunctionName("ProcessQueue")]
        public static void ProcessQueue([QueueTrigger(queueName: "requests")]string request, 
            [DocumentDB(URL_TRACKING, URL_STATS, CreateIfNotExists = true, ConnectionStringSetting ="CosmosDb")]out dynamic doc, 
            TraceWriter log)
        {
            var parsed = request.Split('|');
            var page = string.Empty;
            DateTime date = DateTime.UtcNow;
            var customEvent = string.Empty;
            if (parsed.Length != 3)
            {
                log.Warning($"Bad queue request: {request}");
            }
            else
            {
                // throw exception if this is bad 
                var url = new Uri(parsed[1]);
                // and this 
                date = DateTime.Parse(parsed[2]);
                page = $"{url.Host}{url.AbsolutePath}";
                telemetry.TrackPageView(page);
                log.Info($"Tracked page view {page}");
                if (!string.IsNullOrWhiteSpace(url.Query))
                {
                    customEvent = string.Empty;
                    var queries = HttpUtility.ParseQueryString(url.Query);
                    if (queries["utm_medium"] != null)
                    {
                        customEvent = queries["utm_medium"];
                    }
                    else
                    {
                        if (queries["WT.mc_id"] != null)
                        {
                            var parts = queries["WT.mc_id"].Split('-');
                            customEvent = parts[1];
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(customEvent))
                    {
                        telemetry.TrackEvent(customEvent);
                        log.Info($"Tracked custom event: {customEvent}");
                    }
                }
            }

            // cosmos DB 
            doc = new ExpandoObject();
            doc.id = Guid.NewGuid().ToString();
            doc.page = page;
            doc.count = 1;
            doc.timestamp = date; 
            if (!string.IsNullOrWhiteSpace(customEvent))
            {
                ((IDictionary<string, object>)doc).Add(customEvent, 1);
            }
        }
    }
}