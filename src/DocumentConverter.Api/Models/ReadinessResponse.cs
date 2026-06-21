namespace DocumentConverter.Api.Models;

public sealed class ReadinessResponse
{
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<ReadinessCheckResponse>? Checks { get; init; }
}

public sealed class ReadinessCheckResponse
{
    public string Name { get; init; } = string.Empty;
    public bool IsHealthy { get; init; }
    public string? Message { get; init; }
}
