namespace DocumentConverter.Api.Models;

public sealed class ConversionStatusResponse
{
    public string JobId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string? ResultUrl { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}
