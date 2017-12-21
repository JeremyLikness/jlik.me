using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using jlikme.domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace jlikme.tests
{
    [TestClass]
    public class UtilityTests
    {
        private const string URL = @"https://blog.jeremylikness.com/?test=one&two";
        private const string LONGURL = @"https://blog.jeremylikness.com/page.aspx?test=one&utm_source=jeliknes&WT.mc_id=jeliknes&two=three#frag";
        private const string LONGURLSTRIPPED = @"blog.jeremylikness.com/page.aspx?test=one&two=three#frag";
        private const string BADURL = @"just a walk?in?the&park http://bad";
        private const string CAMPAIGN = "test2";
        private const string WITH_UTM = "&utm_medium=test";
        private const string WITH_UTM_CAMPAIGN = "&utm_medium=test&utm_campaign=test2";
        private const string WITH_WT = "&WT.mc_id=jeliknes";
        private const string WITH_FULL_WT = "&WT.mc_id=test2-test-jeliknes";
        private const string BADDATE = "fig";
        private const string SHORTCODE = "XYZ";
        private const string MEDIUM = "test";
        private static readonly string GOODDATE = DateTime.UtcNow.ToString();
        private static readonly string ENCODED_URL = @"https%3A%2F%2Fblog.jeremylikness.com%2F%3Ftest%3Done%26two";
        private static readonly string HOST = "http://jlik.me";
        private static readonly string PARTITIONKEY = $"{SHORTCODE.First()}";

        [TestMethod]
        public void Given_Digit_When_Encode_Called_Then_Returns_Short_Code()
        {
            var matrix = new List<Tuple<int, string>>()
            {
                Tuple.Create(0, "a"),
                Tuple.Create(25, "z"),
                Tuple.Create(26, "0"),
                Tuple.Create(1000, "12")
            };
            matrix.ForEach(test =>
            {
                Assert.AreEqual(test.Item2, Utility.Encode(test.Item1));
            });
        }

        [TestMethod]
        public void Given_String_When_Cast_To_Partition_Key_Then_Returns_First_Character()
        {
            Assert.AreEqual(PARTITIONKEY, SHORTCODE.AsPartitionKey());
        }

        [TestMethod]
        public void Given_Short_Code_And_Host_When_Cast_To_ShortUrl_Then_Returns_Url()
        {
            Assert.AreEqual($"{HOST}/{SHORTCODE}", SHORTCODE.AsShortUrlWithHost(HOST));
        }

        [TestMethod]
        public async Task Given_Url_When_SaveUrlAsync_Called_Then_Calls_ShortUrlFn()
        {
            var shortUrlFnCalled = false;
            var result = await Utility.SaveUrlAsync(
                URL,
                null,
                HOST,
                () =>
                {
                    shortUrlFnCalled = true;
                    return SHORTCODE;
                },
                msg => { },
                async entry =>
                {
                    await Task.Run(() =>
                    {
                    });
                });
            Assert.IsTrue(shortUrlFnCalled);
        }

        [TestMethod]
        public async Task Given_Url_When_SaveUrlAsync_Called_Then_Calls_SaveFn()
        {
            var saveFnCalled = false;
            var result = await Utility.SaveUrlAsync(
                URL,
                null,
                HOST,
                () => SHORTCODE,                
                msg => { },
                async entry =>
                {
                    await Task.Run(() =>
                    {
                        saveFnCalled = true;
                    });
                });
            Assert.IsTrue(saveFnCalled);
        }

        [TestMethod]
        public async Task Given_Url_When_SaveUrlAsync_Called_Then_PartitionKey_Is_First_Character_Of_ShortCode()
        {
            var result = await Utility.SaveUrlAsync(
                URL,
                null,
                HOST,
                () => SHORTCODE,
                msg => { },
                async (ShortUrl entry) =>
                {
                    await Task.Run(() =>
                    {
                        Assert.AreEqual(PARTITIONKEY, entry.PartitionKey);
                    });
                });            
        }

        [TestMethod]
        public async Task Given_Url_When_SaveUrlAsync_Called_Then_RowKey_Is_ShortCode()
        {
            var result = await Utility.SaveUrlAsync(
                URL,
                null,
                HOST,
                () => SHORTCODE,
                msg => { },
                async (ShortUrl entry) =>
                {
                    await Task.Run(() =>
                    {
                        Assert.AreEqual(SHORTCODE, entry.RowKey);
                    });
                });
        }

        [TestMethod]
        public async Task Given_Url_When_SaveUrlAsync_Called_Then_Url_Is_Set()
        {
            var result = await Utility.SaveUrlAsync(
                URL,
                null,
                HOST,
                () => SHORTCODE,
                msg => { },
                async (ShortUrl entry) =>
                {
                    await Task.Run(() =>
                    {
                        Assert.AreEqual(URL, entry.Url);
                    });
                });
        }

        [TestMethod]
        public async Task Given_Medium_When_SaveUrlAsync_Called_Then_Medium_Is_Set()
        {
            var result = await Utility.SaveUrlAsync(
                URL,
                MEDIUM,
                HOST,
                () => SHORTCODE,
                msg => { },
                async (ShortUrl entry) =>
                {
                    await Task.Run(() =>
                    {
                        Assert.AreEqual(MEDIUM, entry.Medium);
                    });
                });
        }

        [TestMethod]
        public async Task Given_Url_And_Host_When_SaveUrlAsync_Called_Then_Response_Contains_Full_ShortUrl()
        {
            var result = await Utility.SaveUrlAsync(
                URL,
                null,
                HOST,
                () => SHORTCODE,
                msg => { },
                async (ShortUrl entry) =>
                {
                    await Task.Run(() => { });
                });
            Assert.AreEqual(SHORTCODE.AsShortUrlWithHost(HOST), result.ShortUrl);
        }

        [TestMethod]
        public async Task Given_Encoded_Url_When_SaveUrlAsync_Called_Then_Response_Contains_Decoded_LongUrl()
        {
            var result = await Utility.SaveUrlAsync(
                ENCODED_URL,
                null,
                HOST,
                () => SHORTCODE,
                msg => { },
                async (ShortUrl entry) =>
                {
                    await Task.Run(() => { });
                });
            Assert.AreEqual(URL, result.LongUrl);
        }    
        
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Given_Empty_Payload_When_ParseQueuePayload_Called_Then_Throw_Exception()
        {
            var result = Utility.ParseQueuePayload(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Given_Missing_Parts_When_ParseQueuePayload_Called_Then_Throw_Exception()
        {
            var result = Utility.ParseQueuePayload("|");
        }

        [TestMethod]
        [ExpectedException(typeof(UriFormatException))]
        public void Given_Bad_Url_When_ParseQueuePayload_Called_Then_Throw_Exception()
        {
            var result = Utility.ParseQueuePayload($"{SHORTCODE}|{BADURL}|{GOODDATE}");
        }

        [TestMethod]
        [ExpectedException(typeof(FormatException))]
        public void Given_Bad_Date_When_ParseQueuePayload_Called_Then_Throw_Exception()
        {
            var result = Utility.ParseQueuePayload($"{SHORTCODE}|{URL}|{BADDATE}");
        }

        [TestMethod]
        public void Given_Good_Payload_When_ParseQueuePayload_Called_Then_Returns_AnalyticsEntry()
        {
            var result = Utility.ParseQueuePayload($"{SHORTCODE}|{URL}|{GOODDATE}");
            Assert.AreEqual(SHORTCODE, result.ShortUrl);
            Assert.AreEqual(new Uri(URL), result.LongUrl);
            Assert.AreEqual(DateTime.Parse(GOODDATE), result.TimeStamp);
        }

        [TestMethod]
        public void Given_Url_With_Analytics_When_AsPage_Called_Then_Analytics_And_Schema_Are_Stripped()
        {
            var uri = new Uri(LONGURL);
            Assert.AreEqual(LONGURLSTRIPPED, uri.AsPage(HttpUtility.ParseQueryString));
        }

        [TestMethod]
        public void Given_Url_Without_Analytics_When_ExtractCampaignAndMedium_Called_Then_Returns_Empty()
        {
            var uri = new Uri(URL);
            var result = uri.ExtractCampaignAndMedium(HttpUtility.ParseQueryString);
            Assert.AreEqual(string.Empty, result.Item1);
            Assert.AreEqual(string.Empty, result.Item2);
        }

        [TestMethod]
        public void Given_Url_With_Utm_Medium_When_ExtractCampaignAndMedium_Called_Then_Returns_Medium()
        {
            var uri = new Uri($"{URL}{WITH_UTM}");
            var result = uri.ExtractCampaignAndMedium(HttpUtility.ParseQueryString);
            Assert.AreEqual(string.Empty, result.Item1);
            Assert.AreEqual(MEDIUM, result.Item2);
        }

        [TestMethod]
        public void Given_Url_With_Utm_Medium_And_Campaign_When_ExtractCampaignAndMedium_Called_Then_Returns_Medium_And_Campaign()
        {
            var uri = new Uri($"{URL}{WITH_UTM_CAMPAIGN}");
            var result = uri.ExtractCampaignAndMedium(HttpUtility.ParseQueryString);
            Assert.AreEqual(CAMPAIGN, result.Item1);
            Assert.AreEqual(MEDIUM, result.Item2);
        }

        [TestMethod]
        public void Given_Url_With_Incomplete_Wt_When_ExtractCampaignAndMedium_Called_Then_Returns_Empty()
        {
            var uri = new Uri($"{URL}{WITH_WT}");
            var result = uri.ExtractCampaignAndMedium(HttpUtility.ParseQueryString);
            Assert.AreEqual(string.Empty, result.Item1);
            Assert.AreEqual(string.Empty, result.Item2);
        }

        [TestMethod]
        public void Given_Url_With_Wt_When_ExtractCampaignAndMedium_Called_Then_Returns_Campaign_And_Medium()
        {
            var uri = new Uri($"{URL}{WITH_FULL_WT}");
            var result = uri.ExtractCampaignAndMedium(HttpUtility.ParseQueryString);
            Assert.AreEqual(CAMPAIGN, result.Item1);
            Assert.AreEqual(MEDIUM, result.Item2);
        }

    }
}
