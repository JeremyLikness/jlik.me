using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jlikme.domain
{
    public class AnalyticsEntry
    {
        public string ShortUrl { get; set; }
        public Uri LongUrl { get; set; }
        public DateTime TimeStamp { get; set; }
        public Uri Referrer { get; set; }
        public string Agent { get; set; }
    }
}
