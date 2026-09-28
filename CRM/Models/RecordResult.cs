namespace CRM.Models
{
    public class RecordResult
    {
        public bool Success { get; private set; }
        public string? Error { get; private set; }
        public Dictionary<string, object?>? Record { get; private set; }
        public string KeyColumn { get; private set; } = string.Empty;
        public string DisplayLabel { get; private set; } = string.Empty;
        public bool IsEditable { get; private set; }

        public static RecordResult Ok(
            Dictionary<string, object?>? record,
            MasterEditability map,
            string displayLabel)
        {
            return new RecordResult
            {
                Success = true,
                Record = record,
                KeyColumn = map.KeyColumn,
                DisplayLabel = displayLabel,
                IsEditable = map.IsEditable
            };
        }

        public static RecordResult Fail(string error)
        {
            return new RecordResult
            {
                Success = false,
                Error = error
            };
        }
    }
}
