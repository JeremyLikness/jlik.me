using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace jlikme.domain
{
    public static class Utility
    {
        public const string ROBOTS = "robots.txt";
        public const string ROBOT_RESPONSE = "user-agent: *\ndisallow: /";
        public const string URL_TRACKING = "url-tracking";
        public const string URL_STATS = "url-stats";

        public const string UTM_MEDIUM = "utm_medium";
        public const string UTM_SOURCE = "utm_source";
        public const string UTM_CAMPAIGN = "utm_campaign";
        public const string WTMCID = "WT.mc_id";

        public const string TABLE = "urls";
        public const string QUEUE = "requests";
        public const string KEY = "KEY";

        public const string ENV_FALLBACK = "SHORTENER_FALLBACK_URL";
        public const string ENV_SOURCE = "SHORTENER_SOURCE";
        public const string ENV_CAMPAIGN = "SHORTENER_DEFAULT_CAMPAIGN";

        private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

        private static readonly int Base = Alphabet.Length;

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

        public static string AsPartitionKey(this string code)
        {
            return $"{code.First()}";
        }

        public static string AsShortUrlWithHost(this string shortCode, string host)
        {
            return $"{host}/{shortCode}";
        }

        public static async Task<ShortResponse> SaveUrlAsync(
            string url,
            string medium,
            string host,
            Func<string> getShortUrl,
            Action<string> log,
            Func<ShortUrl, Task> save)
        {
            var shortUrl = getShortUrl();
            log($"Short URL for {url} is {shortUrl}");
            var newUrl = new ShortUrl
            {
                PartitionKey = shortUrl.AsPartitionKey(),
                RowKey = shortUrl,
                Medium = medium,
                Url = url
            };
            await save(newUrl);
            return new ShortResponse
            {
                ShortUrl = newUrl.RowKey.AsShortUrlWithHost(host),
                LongUrl = WebUtility.UrlDecode(newUrl.Url)
            };
        }

        public static AnalyticsEntry ParseQueuePayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                throw new ArgumentNullException("payload");
            }
            var parts = payload.Split('|');
            if (parts.Length != 5)
            {
                throw new ArgumentException($"Bad payload: {payload}");
            }
            var entry = new AnalyticsEntry
            {
                ShortUrl = parts[0].ToUpper().Trim(),
                LongUrl = new Uri(parts[1]),
                TimeStamp = DateTime.Parse(parts[2]),
                Referrer = string.IsNullOrWhiteSpace(parts[3]) ? null : new Uri(parts[3]),
                Agent = parts[4]
            };
            return entry;
        }

        public static string AsPage(this Uri uri, Func<string, NameValueCollection> parseQuery)
        {
            var pageUrl = new UriBuilder(uri)
            {
                Port = -1
            };
            var parameters = parseQuery(pageUrl.Query);
            foreach (var check in new[] {
                Utility.UTM_CAMPAIGN,
                Utility.UTM_MEDIUM,
                Utility.UTM_SOURCE,
                Utility.WTMCID })
            {
                if (parameters[check] != null)
                {
                    parameters.Remove(check);
                }
            }
            pageUrl.Query = parameters.ToString();
            return $"{pageUrl.Host}{pageUrl.Path}{pageUrl.Query}{pageUrl.Fragment}";
        }

        public static Tuple<string,string> ExtractCampaignAndMedium(this Uri uri, Func<string, NameValueCollection> parseQuery)
        {
            var campaign = string.Empty;
            var medium = string.Empty;
            if (!string.IsNullOrWhiteSpace(uri.Query))
            {
                var queries = parseQuery(uri.Query);
                if (queries[Utility.WTMCID] != null)
                {
                    var parts = queries[Utility.WTMCID].Split('-');
                    if (parts.Length == 3)
                    {
                        campaign = parts[0];
                        medium = parts[1];
                    }
                }
                else if (queries[Utility.UTM_MEDIUM] != null)
                {
                    medium = queries[Utility.UTM_MEDIUM];
                    if (queries[Utility.UTM_CAMPAIGN] != null)
                    {
                        campaign = queries[Utility.UTM_CAMPAIGN];
                    }
                }                                
            }
            return Tuple.Create(campaign, medium);
        }
    }
}
