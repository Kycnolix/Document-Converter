namespace DocumentConverter.Worker.Models;

public sealed class ConversionResult
{
    public bool Success { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
