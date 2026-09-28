namespace CRM.Models
{
    public class RecordKeyRequest
    {
        public string? TableName { get; set; }
        public string? Key { get; set; }
    }

    public class RecordUpdateRequest : RecordKeyRequest
    {
        public Dictionary<string, object?>? Values { get; set; }
    }
}
