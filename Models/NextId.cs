using Microsoft.WindowsAzure.Storage.Table;

namespace jlikme
{
    public class NextId : TableEntity 
    {
        public int Id { get; set; }
    }
}
