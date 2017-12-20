using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jlikme.domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace jlikme.tests
{
    [TestClass]
    public class UtilityTests
    {
        private const string URL = @"https://blog.jeremylikness.com/?test=one&two";
        private const string SHORTCODE = "XYZ";
        private const string MEDIUM = "test";
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
    }
}
