namespace CRM.Models;

/// <summary>Body posted by the scan box when the operator presses Enter.</summary>
public sealed class BarcodeScanRequest
{
    public string? Barcode { get; set; }
}
