namespace DocumentConverter.Api.Models;

public sealed class ConversionJobMetadata
{
    public string JobId { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string StoredSourceFileName { get; init; } = string.Empty;
    public string ExpectedOutputFileName { get; init; } = string.Empty;
    public string TargetFormat { get; init; } = "pdf";
    public DateTimeOffset CreatedAtUtc { get; init; }
}
