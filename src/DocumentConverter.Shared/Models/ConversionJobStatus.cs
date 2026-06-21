namespace DocumentConverter.Shared.Models;

public static class ConversionJobStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
    public const string Unsupported = "Unsupported";
    public const string Unknown = "Unknown";
}
