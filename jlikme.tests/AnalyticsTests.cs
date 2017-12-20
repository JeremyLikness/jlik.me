using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Web;
using jlikme.domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace jlikme.tests
{
    [TestClass]
    public class AnalyticsTests
    {
        private const string GOODURL = "https://blog.jeremylikness.com";
        private const string ANCHOR = "#page";
        private const string BADURL = @"just a walk?in?the&park http://bad";
        private const string SOURCE = "jeliknes";
        private const string HOST = @"https://jlik.me";
        private const string CAMPAIGN = "testcampaign";
        private const string MEDIUM = "test";
        private const string MEDIUM2 = "test2";
        private const string UTM_CHECK = "utm";
        private const string WT_CHECK = "WT.mc_id";

        private static readonly string URL_WITH_ANCHOR = $"{GOODURL}{ANCHOR}";
        private static readonly string UTM_SOURCE_FRAGMENT = $"utm_source={SOURCE}";
        private static readonly string UTM_MEDIUM_FRAGMENT = $"utm_medium={MEDIUM}";
        private static readonly string UTM_MEDIUM2_FRAGMENT = $"utm_medium={MEDIUM2}";
        private static readonly string UTM_CAMPAIGN_FRAGMENT = $"utm_campaign={CAMPAIGN}";
        private static readonly string WT_LINK = $"{CAMPAIGN}-{MEDIUM}-{SOURCE}";
        private static readonly string WT_LINK2 = $"{CAMPAIGN}-{MEDIUM2}-{SOURCE}";
        private static readonly string URL_WITH_ANCHOR_TAGGED = $"{GOODURL}/?{WT_CHECK}={WT_LINK}{ANCHOR}";

        private static readonly string[] MEDIUMS = new string[] { MEDIUM };

        private Analytics _analytics;
        private ShortRequest _request;

        [TestInitialize]
        public void Setup()
        {
            _analytics = new Analytics();
            _request = new ShortRequest();
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void Given_Medium_No_Type_Then_Validate_Should_Throw_Exception()
        {
            _request.Input = GOODURL;
            _request.Mediums = MEDIUMS;
            _analytics.Validate(_request);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void Given_Utm_No_Medium_Then_Validate_Should_Throw_Exception()
        {
            _request.Input = GOODURL;
            _request.TagUtm = true;
            _analytics.Validate(_request);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void Given_Wt_No_Medium_Then_Validate_Should_Throw_Exception()
        {
            _request.Input = GOODURL;
            _request.TagWt = true;
            _analytics.Validate(_request);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void Given_Wt_And_Utm_No_Medium_Then_Validate_Should_Throw_Exception()
        {
            _request.Input = GOODURL;
            _request.TagWt = true;
            _request.TagUtm = true;
            _analytics.Validate(_request);
        }

        [TestMethod]
        public void Given_Nothing_But_Url_Then_Validate_Should_Return_False()
        {
            _request.Input = GOODURL;
            Assert.IsFalse(_analytics.Validate(_request));
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void Given_No_Url_Then_Validate_Should_Throw_Exception()
        {
            _analytics.Validate(_request);
        }

        [TestMethod]
        [ExpectedException(typeof(UriFormatException))]
        public void Given_Bad_Url_Then_Validate_Should_Throw_Exception()
        {
            _request.Input = BADURL;
            _analytics.Validate(_request);
        }

        [TestMethod]
        public void Given_Wt_And_Medium_Then_Validate_Should_Return_True()
        {
            _request.Input = GOODURL;
            _request.TagWt = true;
            _request.Mediums = MEDIUMS;
            Assert.IsTrue(_analytics.Validate(_request));
        }

        [TestMethod]
        public void Given_Utm_And_Medium_Then_Validate_Should_Return_True()
        {
            _request.Input = GOODURL;
            _request.TagUtm = true;
            _request.Mediums = MEDIUMS;
            Assert.IsTrue(_analytics.Validate(_request));
        }

        [TestMethod]
        public void Given_Utm_And_Wt_And_Medium_Then_Validate_Should_Return_True()
        {
            _request.Input = GOODURL;
            _request.TagUtm = true;
            _request.TagWt = true;
            _request.Mediums = MEDIUMS;
            Assert.IsTrue(_analytics.Validate(_request));
        }

        [TestMethod]
        public async Task Given_Utm_And_Medium_Then_BuildAsync_Generates_Utm_Link()
        {
            _request.Input = GOODURL;
            _request.Mediums = MEDIUMS;
            _request.Campaign = CAMPAIGN;
            _request.TagUtm = true;
            var results = await _analytics.BuildAsync(
                _request,
                SOURCE,
                HOST,
                () => "X",
                async entity => await Task.Run(() => { }),
                msg => { },
                HttpUtility.ParseQueryString
                );
            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_CAMPAIGN_FRAGMENT));
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_SOURCE_FRAGMENT));
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_MEDIUM_FRAGMENT));
        }

        [TestMethod]
        public async Task Given_No_Wt_And_Medium_Then_BuildAsync_Does_Not_Generate_Wt_Link()
        {
            _request.Input = GOODURL;
            _request.Mediums = MEDIUMS;
            _request.Campaign = CAMPAIGN;
            _request.TagUtm = true;
            var results = await _analytics.BuildAsync(
                _request,
                SOURCE,
                HOST,
                () => "X",
                async entity => await Task.Run(() => { }),
                msg => { },
                HttpUtility.ParseQueryString
                );
            Assert.AreEqual(1, results.Count);
            Assert.IsFalse(results[0].LongUrl.Contains(WT_CHECK));
        }

        [TestMethod]
        public async Task Given_Wt_And_Medium_Then_BuildAsync_Generates_Wt_Link()
        {
            _request.Input = GOODURL;
            _request.Mediums = MEDIUMS;
            _request.Campaign = CAMPAIGN;
            _request.TagWt = true;
            var results = await _analytics.BuildAsync(
                _request,
                SOURCE,
                HOST,
                () => "X",
                async entity => await Task.Run(() => { }),
                msg => { },
                HttpUtility.ParseQueryString
                );
            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].LongUrl.Contains(WT_LINK));
        }

        [TestMethod]
        public async Task Given_No_Utm_And_Medium_Then_BuildAsync_Does_Not_Generate_Utm_Link()
        {
            _request.Input = GOODURL;
            _request.Mediums = MEDIUMS;
            _request.Campaign = CAMPAIGN;
            _request.TagWt = true;
            var results = await _analytics.BuildAsync(
                _request,
                SOURCE,
                HOST,
                () => "X",
                async entity => await Task.Run(() => { }),
                msg => { },
                HttpUtility.ParseQueryString
                );
            Assert.AreEqual(1, results.Count);
            Assert.IsFalse(results[0].LongUrl.Contains(UTM_CHECK));
        }

        [TestMethod]
        public async Task Given_Wt_And_Utm_And_Medium_Then_BuildAsync_Generates_Utm_And_Wt_Link()
        {
            _request.Input = GOODURL;
            _request.Mediums = MEDIUMS;
            _request.Campaign = CAMPAIGN;
            _request.TagWt = _request.TagUtm = true;
            var results = await _analytics.BuildAsync(
                _request,
                SOURCE,
                HOST,
                () => "X",
                async entity => await Task.Run(() => { }),
                msg => { },
                HttpUtility.ParseQueryString
                );
            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_CAMPAIGN_FRAGMENT));
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_SOURCE_FRAGMENT));
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_MEDIUM_FRAGMENT));
            Assert.IsTrue(results[0].LongUrl.Contains(WT_LINK));
        }

        [TestMethod]
        public async Task Given_Utm_And_Multiple_Mediums_Then_BuildAsync_Generates_Multiple_Utm_Links()
        {
            _request.Input = GOODURL;
            _request.Mediums = new string[] { MEDIUM, MEDIUM2 };
            _request.Campaign = CAMPAIGN;
            _request.TagUtm = true;
            var results = await _analytics.BuildAsync(
                _request,
                SOURCE,
                HOST,
                () => "X",
                async entity => await Task.Run(() => { }),
                msg => { },
                HttpUtility.ParseQueryString
                );
            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_CAMPAIGN_FRAGMENT));
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_SOURCE_FRAGMENT));
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_MEDIUM_FRAGMENT));
            Assert.IsFalse(results[0].LongUrl.Contains(WT_CHECK));
            Assert.IsTrue(results[1].LongUrl.Contains(UTM_CAMPAIGN_FRAGMENT));
            Assert.IsTrue(results[1].LongUrl.Contains(UTM_SOURCE_FRAGMENT));
            Assert.IsTrue(results[1].LongUrl.Contains(UTM_MEDIUM2_FRAGMENT));
            Assert.IsFalse(results[1].LongUrl.Contains(WT_CHECK));
        }

        [TestMethod]
        public async Task Given_Wt_And_Multiple_Mediums_Then_BuildAsync_Generates_Multiple_Wt_Links()
        {
            _request.Input = GOODURL;
            _request.Mediums = new string[] { MEDIUM, MEDIUM2 };
            _request.Campaign = CAMPAIGN;
            _request.TagWt = true;
            var results = await _analytics.BuildAsync(
                _request,
                SOURCE,
                HOST,
                () => "X",
                async entity => await Task.Run(() => { }),
                msg => { },
                HttpUtility.ParseQueryString
                );
            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results[0].LongUrl.Contains(WT_LINK));
            Assert.IsFalse(results[0].LongUrl.Contains(UTM_CHECK));
            Assert.IsTrue(results[1].LongUrl.Contains(WT_LINK2));
            Assert.IsFalse(results[1].LongUrl.Contains(UTM_CHECK));
        }

        [TestMethod]
        public async Task Given_Utm_And_Wt_And_Multiple_Mediums_Then_BuildAsync_Generates_Multiple_Links()
        {
            _request.Input = GOODURL;
            _request.Mediums = new string[] { MEDIUM, MEDIUM2 };
            _request.Campaign = CAMPAIGN;
            _request.TagUtm = _request.TagWt = true;
            var results = await _analytics.BuildAsync(
                _request,
                SOURCE,
                HOST,
                () => "X",
                async entity => await Task.Run(() => { }),
                msg => { },
                HttpUtility.ParseQueryString
                );
            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_CAMPAIGN_FRAGMENT));
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_SOURCE_FRAGMENT));
            Assert.IsTrue(results[0].LongUrl.Contains(UTM_MEDIUM_FRAGMENT));
            Assert.IsTrue(results[1].LongUrl.Contains(UTM_CAMPAIGN_FRAGMENT));
            Assert.IsTrue(results[1].LongUrl.Contains(UTM_SOURCE_FRAGMENT));
            Assert.IsTrue(results[1].LongUrl.Contains(UTM_MEDIUM2_FRAGMENT));
            Assert.IsTrue(results[0].LongUrl.Contains(WT_LINK));
            Assert.IsTrue(results[1].LongUrl.Contains(WT_LINK2));
        }

        [TestMethod]
        public async Task Given_Code_Strategy_When_BuildAsync_Called_Then_Passes_To_Utility()
        {
            _request.Input = GOODURL;
            _request.Mediums = MEDIUMS;
            _request.Campaign = CAMPAIGN;
            _request.TagUtm = true;
            var results = await _analytics.BuildAsync(
                _request,
                SOURCE,
                HOST,
                () => "X",
                async entity => await Task.Run(() => { }),
                msg => { },
                HttpUtility.ParseQueryString
                );
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(results[0].ShortUrl, "X".AsShortUrlWithHost(HOST));
        }

        [TestMethod]
        public async Task Given_Url_With_Anchor_When_BuildAsync_Called_Then_URL_Formatted_Properly()
        {
            _request.Input = URL_WITH_ANCHOR;
            _request.Mediums = MEDIUMS;
            _request.Campaign = CAMPAIGN;
            _request.TagWt = true;
            var results = await _analytics.BuildAsync(
                _request,
                SOURCE,
                HOST,
                () => "X",
                async entity => await Task.Run(() => { }),
                msg => { },
                HttpUtility.ParseQueryString
                );
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(URL_WITH_ANCHOR_TAGGED, results[0].LongUrl);
        }
    }
}
