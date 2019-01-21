using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jlikme.domain
{
    public class ShortRequest
    {
        public bool? TagUtm { get; set; }

        public bool? TagWt { get; set; }

        public string Campaign { get; set; }

        public string[] Mediums { get; set; }

        public string Input { get; set; }
    }
}
