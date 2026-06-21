namespace DocumentConverter.Shared.Models;

public sealed class ConversionJobMetadata
{
    public string JobId { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredSourceFileName { get; set; } = string.Empty;
    public string ExpectedOutputFileName { get; set; } = string.Empty;
    public string TargetFormat { get; set; } = "pdf";
    public string Status { get; set; } = ConversionJobStatus.Pending;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? SourceExtension { get; set; }
    public long? SourceSizeBytes { get; set; }
}
