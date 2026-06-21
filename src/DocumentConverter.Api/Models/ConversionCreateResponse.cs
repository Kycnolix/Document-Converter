namespace DocumentConverter.Api.Models;

public sealed class ConversionCreateResponse
{
    public string JobId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? ResultUrl { get; init; }
}
