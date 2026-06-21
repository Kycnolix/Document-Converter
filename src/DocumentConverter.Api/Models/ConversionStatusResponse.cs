namespace DocumentConverter.Api.Models;

public sealed class ConversionStatusResponse
{
    public string JobId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string? ResultUrl { get; init; }
}
