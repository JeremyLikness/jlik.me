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
using jlikme.domain;
using System.IO;
using System.Net.Http.Headers;
using System.Collections;
using System.Web.Configuration;
using System.Collections.Specialized;

namespace jlikme
{
    public static class FunctionHost
    {
        public static TelemetryClient telemetry = new TelemetryClient()
        {
#if !DEBUG
            InstrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")
#endif
        };

        // this is redirect target when the short URL isn't found
        public static readonly string FallbackUrl = Environment.GetEnvironmentVariable(Utility.ENV_FALLBACK) ??
            "https://blog.jeremylikness.com/?utm_source=jeliknes&utm_medium=redirect&utm_campaign=jlik_me";

        // for tagging, the "utm_source" or source part of WebTrends tag 
        public static readonly string Source = Environment.GetEnvironmentVariable(Utility.ENV_SOURCE) ??
            "jeliknes";

        // default campaign, for tagging 
        public static readonly string DefaultCampaign = Environment.GetEnvironmentVariable(Utility.ENV_CAMPAIGN) ??
            "link";

        private static async Task TrackDependencyAsync(
            string dependency,
            string command,
            Func<Task> commandAsync,
            Func<bool> success)
        {
#if DEBUG
            return;
#else
            var startTime = DateTime.UtcNow;
            var timer = System.Diagnostics.Stopwatch.StartNew();

            await commandAsync();

            telemetry.TrackDependency(dependency, command, startTime, timer.Elapsed, success());
#endif
        }

        private static HttpResponseMessage SecurityCheck(HttpRequestMessage req)
        {
            return req.IsLocal() || req.RequestUri.Scheme == "https" ? null :
                req.CreateResponse(HttpStatusCode.Forbidden);
        }

        // returns a single page application to build links
        [FunctionName("Utility")]
        public static HttpResponseMessage Admin([HttpTrigger(AuthorizationLevel.Anonymous, "get")]HttpRequestMessage req,
            TraceWriter log)
        {
            const string PATH = "LinkShortener.html";

            var result = SecurityCheck(req);
            if (result != null)
            {
                return result;
            }

            var scriptPath = Path.Combine(Environment.CurrentDirectory, "www");
            if (!Directory.Exists(scriptPath))
            {
                scriptPath = Path.Combine(
                    Environment.GetEnvironmentVariable("HOME", EnvironmentVariableTarget.Process),
                    @"site\wwwroot\www");
            }
            var filePath = Path.GetFullPath(Path.Combine(scriptPath, PATH));
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
            [Table(Utility.TABLE, "1", Utility.KEY, Take = 1)]NextId keyTable,
            [Table(Utility.TABLE)]CloudTable tableOut,
            TraceWriter log)
        {
            log.Info($"C# triggered function called with req: {req}");

            if (req == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            var check = SecurityCheck(req);
            if (check != null)
            {
                return check;
            }

            ShortRequest input = await req.Content.ReadAsAsync<ShortRequest>();

            if (input == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            try
            {
                var result = new List<ShortResponse>();
                var analytics = new Analytics();

                // determine whether or not to process analytics tags
                bool tagMediums = analytics.Validate(input);

                var campaign = string.IsNullOrWhiteSpace(input.Campaign) ? DefaultCampaign : input.Campaign;
                var url = input.Input.Trim();
                var utm = analytics.TagUtm(input);
                var wt = analytics.TagWt(input);

                log.Info($"URL: {url} Tag UTM? {utm} Tag WebTrends? {wt}");
                log.Info($"Current key: {keyTable.Id}");

                // get host for building short URL 
                var host = req.RequestUri.GetLeftPart(UriPartial.Authority);

                // strategy for getting a new code 
                string getCode() => Utility.Encode(keyTable.Id++);

                // strategy for logging 
                void logFn(string msg) => log.Info(msg);

                // strategy to save the key 
                async Task saveKeyAsync()
                {
                    var operation = TableOperation.Replace(keyTable);
                    await tableOut.ExecuteAsync(operation);
                }

                // strategy to insert the new short url entry
                async Task saveEntryAsync(TableEntity entry)
                {
                    var operation = TableOperation.Insert(entry);
                    await tableOut.ExecuteAsync(operation);
                }

                // strategy to create a new URL and track the dependencies
                async Task saveWithTelemetryAsync(TableEntity entry)
                {
                    await TrackDependencyAsync(
                        "AzureTableStorageInsert",
                        "Insert",
                        async () => await saveEntryAsync(entry),
                        () => true);
                    await TrackDependencyAsync(
                        "AzureTableStorageUpdate",
                        "Update",
                        async () => await saveKeyAsync(),
                        () => true);
                }

                if (tagMediums)
                {
                    // this will result in multiple entries depending on the number of 
                    // mediums passed in 
                    result.AddRange(await analytics.BuildAsync(
                        input,
                        Source,
                        host,
                        getCode,
                        saveWithTelemetryAsync,
                        logFn,
                        HttpUtility.ParseQueryString));
                }
                else
                {
                    // no tagging, just pass-through the URL
                    result.Add(await Utility.SaveUrlAsync(
                        url,
                        null,
                        host,
                        getCode,
                        logFn,
                        saveWithTelemetryAsync));
                }

                log.Info($"Done.");
                return req.CreateResponse(HttpStatusCode.OK, result);
            }
            catch (Exception ex)
            {
                log.Error("An unexpected error was encountered.", ex);
                return req.CreateErrorResponse(HttpStatusCode.BadRequest, ex);
            }
        }

        [FunctionName(name: "UrlRedirect")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post",
            Route = "UrlRedirect/{shortUrl}")]HttpRequestMessage req,
            [Table(tableName: Utility.TABLE)]CloudTable inputTable,
            string shortUrl,
            [Queue(queueName: Utility.QUEUE)]IAsyncCollector<string> queue,
            TraceWriter log)
        {
            log.Info($"C# HTTP trigger function processed a request for shortUrl {shortUrl}");

            shortUrl = shortUrl.ToLower();

            if (shortUrl == Utility.ROBOTS)
            {
                log.Info("Request for robots.txt.");
                var robotResponse = req.CreateResponse(HttpStatusCode.OK, Utility.ROBOT_RESPONSE, "text/plain");
                return robotResponse;
            }

            var redirectUrl = FallbackUrl;

            if (!String.IsNullOrWhiteSpace(shortUrl))
            {
                shortUrl = shortUrl.Trim().ToLower();

                var partitionKey = shortUrl.AsPartitionKey();

                log.Info($"Searching for partition key {partitionKey} and row {shortUrl}.");

                TableResult result = null;

                await TrackDependencyAsync("AzureTableStorage", "Retrieve", async () =>
                {
                    TableOperation operation = TableOperation.Retrieve<ShortUrl>(partitionKey, shortUrl);
                    result = await inputTable.ExecuteAsync(operation);
                },
                () => result != null && result.Result != null);

                if (result.Result is ShortUrl fullUrl)
                {
                    log.Info($"Found it: {fullUrl.Url}");
                    redirectUrl = WebUtility.UrlDecode(fullUrl.Url);
                }
                var referrer = string.Empty;
                if (req.Headers.Referrer != null)
                {
                    log.Info($"Referrer: {req.Headers.Referrer.ToString()}");
                    referrer = req.Headers.Referrer.ToString();
                }
                log.Info($"User agent: {req.Headers.UserAgent.ToString()}");
                await queue.AddAsync($"{shortUrl}|{redirectUrl}|{DateTime.UtcNow}|{referrer}|{req.Headers.UserAgent.ToString().Replace('|', '^')}");
            }
            else
            {
                telemetry.TrackEvent("Bad Link, resorting to fallback.");
            }

            var res = req.CreateResponse(HttpStatusCode.Redirect);
            res.Headers.Add("Location", redirectUrl);
            return res;
        }

        [FunctionName("KeepAlive")]
        public static void KeepAlive([TimerTrigger(scheduleExpression: "0 */4 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info("Keep-Alive invoked.");
        }

        [FunctionName("ProcessQueue")]
        public static void ProcessQueue([QueueTrigger(queueName: Utility.QUEUE)]string request,
            [DocumentDB(Utility.URL_TRACKING, Utility.URL_STATS, CreateIfNotExists = true, ConnectionStringSetting = "CosmosDb")]out dynamic doc,
            TraceWriter log)
        {
            try
            {
                AnalyticsEntry parsed = Utility.ParseQueuePayload(request);
                var page = parsed.LongUrl.AsPage(HttpUtility.ParseQueryString);

                telemetry.TrackPageView(page);
                log.Info($"Tracked page view {page}");

                var analytics = parsed.LongUrl.ExtractCampaignAndMedium(HttpUtility.ParseQueryString);
                var campaign = analytics.Item1;
                var medium = analytics.Item2;

                if (!string.IsNullOrWhiteSpace(medium))
                {
                    telemetry.TrackEvent(medium);
                    log.Info($"Tracked custom event: {medium}");
                }

                // cosmos DB 
                var normalize = new[] { '/' };
                doc = new ExpandoObject();
                doc.id = Guid.NewGuid().ToString();
                doc.page = page.TrimEnd(normalize);
                if (!string.IsNullOrWhiteSpace(parsed.ShortUrl))
                {
                    doc.shortUrl = parsed.ShortUrl;
                }
                if (!string.IsNullOrWhiteSpace(campaign))
                {
                    doc.campaign = campaign;
                }
                if (parsed.Referrer != null)
                {
                    doc.referrerUrl = parsed.Referrer.AsPage(HttpUtility.ParseQueryString);
                    doc.referrerHost = parsed.Referrer.DnsSafeHost;
                }
                if (!string.IsNullOrWhiteSpace(parsed.Agent))
                {
                    doc.agent = parsed.Agent;
                    try
                    {
                        var capabilities = new HttpBrowserCapabilities()
                        {
                            Capabilities = new Hashtable { { string.Empty, parsed.Agent } }
                        };
                        var factory = new BrowserCapabilitiesFactory();
                        factory.ConfigureBrowserCapabilities(new NameValueCollection(), capabilities);
                        factory.ConfigureCustomCapabilities(new NameValueCollection(), capabilities);
                        if (!string.IsNullOrEmpty(capabilities.Browser))
                        {
                            var browser = capabilities.Browser;
                            var version = capabilities.MajorVersion;
                            var browserVersion = $"{browser} {capabilities.MajorVersion}";
                            doc.browser = browser;
                            doc.browserVersion = version;
                            doc.browserWithVersion = browserVersion;
                        }
                        if (capabilities.Crawler)
                        {
                            doc.crawler = 1;
                        }
                        if (capabilities.IsMobileDevice)
                        {
                            doc.mobile = 1;
                            var manufacturer = capabilities.MobileDeviceManufacturer;
                            var model = capabilities.MobileDeviceModel;
                            doc.mobileManufacturer = manufacturer;
                            doc.mobileModel = model;
                            doc.mobileDevice = $"{manufacturer} {model}";
                        }
                        else
                        {
                            doc.desktop = 1;
                        }
                        if (!string.IsNullOrWhiteSpace(capabilities.Platform))
                        {
                            doc.platform = capabilities.Platform;
                        }

                    }
                    catch (Exception ex)
                    {
                        log.Error($"Error parsing user agent [{parsed.Agent}]", ex);
                    }
                }
                doc.count = 1;
                doc.timestamp = parsed.TimeStamp;
                doc.host = parsed.LongUrl.DnsSafeHost;
                if (!string.IsNullOrWhiteSpace(medium))
                {
                    ((IDictionary<string, object>)doc).Add(medium, 1);
                }
                log.Info($"CosmosDB: {doc.id}|{doc.page}|{parsed.ShortUrl}|{campaign}|{medium}");
            }
            catch (Exception ex)
            {
                log.Error("An unexpected error occurred", ex);
                throw;
            }
        }

        [FunctionName(name: "UpdateTwitter")]
        public static async Task<HttpResponseMessage> Twitter([HttpTrigger(AuthorizationLevel.Function, "post",
            Route = "UpdateTwitter/{id}")]HttpRequestMessage req,
            [DocumentDB(Utility.URL_TRACKING, Utility.URL_STATS, CreateIfNotExists = false, ConnectionStringSetting = "CosmosDb", Id = "{id}")]dynamic doc,
            string id,
            TraceWriter log)
        {
            var result = SecurityCheck(req);
            if (result != null)
            {
                return result;
            }
            if (doc == null)
            {
                log.Error($"Doc not found with id: {id}.");
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
            var link = await req.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(link))
            {
                doc.referralTweet = link;
            }
            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}