using Microsoft.WindowsAzure.Storage.Table;

namespace jlikme.domain
{
    public class NextId : TableEntity 
    {
        public int Id { get; set; }
    }
}
