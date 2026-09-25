using System.Text.Json.Serialization;

namespace CRM.Models
{
    public class CategoryOption
    {
        [JsonPropertyName("category1")]
        public string? Category1 { get; set; }

        [JsonPropertyName("category2")]
        public string? Category2 { get; set; }
    }

    public class ItemRaw
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("category1")]
        public string? Category1 { get; set; }

        [JsonPropertyName("category2")]
        public string? Category2 { get; set; }

        [JsonPropertyName("unit")]
        public string? Unit { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("hsncode")]
        public string? Hsncode { get; set; }

        [JsonPropertyName("modifyDate")]
        public DateTime? Modify_Date { get; set; }
    }
}