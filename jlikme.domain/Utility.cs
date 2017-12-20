using System;
using System.Collections.Generic;
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
    }
}
