namespace DocumentConverter.Worker.Models;

public sealed class ConversionResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
