using Microsoft.WindowsAzure.Storage.Table;

namespace jlikme.domain
{
    public class ShortUrl : TableEntity
    {
        public string Url { get; set; }
        public string Medium { get; set; }
    }
}
