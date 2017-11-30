using System.Linq;
using System.Net;
using System.Net.Http;
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
using jlikme.Models;
using System.IO;
using System.Net.Http.Headers;

namespace jlikme
{
    public static class FunctionHost
    {
        public static TelemetryClient telemetry = new TelemetryClient()
        {
            InstrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")
        };

        public const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        public const string ROBOTS = "robots.txt";
        public const string ROBOT_RESPONSE = "User-agent: *\nDisallow: /";
        public const string FALLBACK_URL = "https://blog.jeremylikness.com/?utm_source=jeliknes&utm_medium=redirect&utm_campaign=jlik_me";
        public const string KEEP_ALIVE = "xxxxxx";
        public const string KEEP_ALIVE_URL = "https://jlikme.azurewebsites.net/api/UrlRedirect/xxxxxx";
        public const string URL_TRACKING = "url-tracking";
        public const string URL_STATS = "url-stats";
        public const string SOURCE = "jeliknes";
        public const string DEFAULT_CAMPAIGN = "link";

        public const string UTM_MEDIUM = "utm_medium";
        public const string UTM_SOURCE = "utm_source";
        public const string UTM_CAMPAIGN = "utm_campaign";
        public const string WTMCID = "WT.mc_id";

        public const string TABLE = "urls";
        public const string QUEUE = "requests";
        public const string KEY = "KEY";

        public static readonly int Base = Alphabet.Length;

        public static string Encode(int i)
        {
            if (i == 0)
                return Alphabet[0].ToString();
            var s = string.Empty;
            while (i > 0)
            {
                s += Alphabet[i % Base];
                i = i / Base;
            }

            return string.Join(string.Empty, s.Reverse());
        }

        [FunctionName("Utility")]
        public static HttpResponseMessage Admin([HttpTrigger(AuthorizationLevel.Anonymous, "get")]HttpRequestMessage req,
            TraceWriter log)
        {
            var path = "LinkShortener.html";
           
            var scriptPath = Path.Combine(Environment.CurrentDirectory, "www");
            if (!Directory.Exists(scriptPath))
            {
                scriptPath = Path.Combine(
                    Environment.GetEnvironmentVariable("HOME", EnvironmentVariableTarget.Process), 
                    @"site\wwwroot\www");
            }
            var filePath = Path.GetFullPath(Path.Combine(scriptPath, path));
            if (!File.Exists(filePath))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            log.Info($"Attempting to retrieve file at path {filePath}.");
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var stream = new FileStream(filePath, FileMode.Open);
            response.Content = new StreamContent(stream);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return response;
        }

        [FunctionName("ShortenUrl")]
        public static async Task<HttpResponseMessage> ShortenUrl(
            [HttpTrigger(AuthorizationLevel.Function, "post")]HttpRequestMessage req, 
            [Table(TABLE, "1", KEY, Take = 1)]NextId keyTable, 
            [Table(TABLE)]CloudTable tableOut, 
            TraceWriter log)
        {
            log.Info($"C# triggered function called with req: {req}");

            if (req == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            ShortRequest input = await req.Content.ReadAsAsync<ShortRequest>();

            if (input == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            var result = new List<ShortResponse>();
            var url = input.Input;
            var campaign = string.IsNullOrWhiteSpace(input.Campaign) ? DEFAULT_CAMPAIGN : input.Campaign;
            bool tagMediums = input.Mediums != null && input.Mediums.Any();
            var utm = input.TagUtm.HasValue && input.TagUtm.Value;
            var wt = input.TagWt.HasValue && input.TagWt.Value;
            var tag = utm || wt; 

            if (tagMediums && !tag)
            {
                throw new Exception("Must choose either UTM or WT when mediums are passed.");
            }

            if (tag && !tagMediums)
            {
                throw new Exception("Can't specify a tag without at least one medium.");
            }

            log.Info($"URL: {url} Tag UTM? {utm} Tag WebTrends? {wt}");

            if (String.IsNullOrWhiteSpace(url))
            {
                throw new Exception("Need a URL to shorten!");
            }

            log.Info($"Current key: {keyTable.Id}");

            var host = req.RequestUri.GetLeftPart(UriPartial.Authority);

            if (tagMediums)
            {
                foreach (var medium in input.Mediums)
                {
                    var uri = new UriBuilder(url);
                    uri.Port = -1;
                    var parameters = HttpUtility.ParseQueryString(uri.Query);
                    if (utm)
                    {
                        parameters.Add(UTM_SOURCE, SOURCE);
                        parameters.Add(UTM_MEDIUM, medium);
                        parameters.Add(UTM_CAMPAIGN, input.Campaign);
                    }
                    if (wt)
                    {
                        parameters.Add(WTMCID, $"{input.Campaign}-{medium}-{SOURCE}");
                    }
                    uri.Query = parameters.ToString();
                    var mediumUrl = uri.ToString();
                    var shortUrl = Encode(keyTable.Id++);
                    log.Info($"Short URL for {mediumUrl} is {shortUrl}");
                    var newUrl = new ShortUrl
                    {
                        PartitionKey = $"{shortUrl.First()}",
                        RowKey = $"{shortUrl}",
                        Medium = medium,
                        Url = mediumUrl
                    };
                    var multiAdd = TableOperation.Insert(newUrl);
                    await tableOut.ExecuteAsync(multiAdd);
                    result.Add(new ShortResponse
                    {
                        ShortUrl = $"{host}/{newUrl.RowKey}",
                        LongUrl = WebUtility.UrlDecode(newUrl.Url)
                    });
                }
            }
            else
            {
                var shortUrl = Encode(keyTable.Id++);
                log.Info($"Short URL for {url} is {shortUrl}");
                var newUrl = new ShortUrl
                {
                    PartitionKey = $"{shortUrl.First()}",
                    RowKey = $"{shortUrl}",
                    Url = url
                };
                var singleAdd = TableOperation.Insert(newUrl);
                await tableOut.ExecuteAsync(singleAdd);
                result.Add(new ShortResponse
                {
                    ShortUrl = $"{host}/{newUrl.RowKey}",
                    LongUrl = WebUtility.UrlDecode(newUrl.Url)
                });
            }

            var operation = TableOperation.Replace(keyTable);
            await tableOut.ExecuteAsync(operation);

            log.Info($"Done.");
            return req.CreateResponse(HttpStatusCode.OK, result);
        }

        [FunctionName(name: "UrlRedirect")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", 
            Route = "UrlRedirect/{shortUrl}")]HttpRequestMessage req, 
            [Table(tableName: TABLE)]CloudTable inputTable, 
            string shortUrl,
            [Queue(queueName: QUEUE)]IAsyncCollector<string> queue,
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
        public static void ProcessQueue([QueueTrigger(queueName: QUEUE)]string request, 
            [DocumentDB(URL_TRACKING, URL_STATS, CreateIfNotExists = true, ConnectionStringSetting ="CosmosDb")]out dynamic doc, 
            TraceWriter log)
        {
            var parsed = request.Split('|');
            var page = string.Empty;
            var shortUrl = string.Empty;
            var campaign = string.Empty;
            DateTime date = DateTime.UtcNow;
            var customEvent = string.Empty;
            if (parsed.Length != 3)
            {
                log.Warning($"Bad queue request: {request}");
            }
            else
            {
                shortUrl = parsed[0].ToUpper().Trim();
                // throw exception if this is bad 
                var url = new Uri(parsed[1]);
                var pageUrl = new UriBuilder(parsed[1]);
                var parameters = HttpUtility.ParseQueryString(pageUrl.Query);
                foreach(var check in new [] { UTM_CAMPAIGN, UTM_MEDIUM, UTM_SOURCE, WTMCID })
                {
                    if (parameters[check] != null)
                    {
                        parameters.Remove(check);
                    }
                }
                pageUrl.Query = parameters.ToString();
                // and this 
                date = DateTime.Parse(parsed[2]);
                page = $"{pageUrl.ToString()}";
                telemetry.TrackPageView(page);
                log.Info($"Tracked page view {page}");
                if (!string.IsNullOrWhiteSpace(url.Query))
                {
                    customEvent = string.Empty;
                    var queries = HttpUtility.ParseQueryString(url.Query);
                    if (queries[UTM_MEDIUM] != null)
                    {
                        customEvent = queries[UTM_MEDIUM];
                        if (queries[UTM_CAMPAIGN] != null)
                        {
                            campaign = queries[UTM_CAMPAIGN];
                        }
                    }
                    else
                    {
                        if (queries[WTMCID] != null)
                        {
                            var parts = queries[WTMCID].Split('-');
                            campaign = parts[0];
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
            var normalize = new[] { '/' };
            doc = new ExpandoObject();
            doc.id = Guid.NewGuid().ToString();
            doc.page = page.TrimEnd(normalize);
            if (!string.IsNullOrWhiteSpace(shortUrl))
            {
                doc.shortUrl = shortUrl;
            }
            if (!string.IsNullOrWhiteSpace(campaign))
            {
                doc.campaign = campaign;
            }
            doc.count = 1;
            doc.timestamp = date; 
            if (!string.IsNullOrWhiteSpace(customEvent))
            {
                ((IDictionary<string, object>)doc).Add(customEvent, 1);
            }
            log.Info($"CosmosDB: {doc.id}|{doc.page}|{shortUrl}|{campaign}|{customEvent}");
        }
    }
}