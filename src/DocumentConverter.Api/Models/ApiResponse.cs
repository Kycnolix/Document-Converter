namespace DocumentConverter.Api.Models;

public sealed class ApiResponse<T>
{
    public string? Message { get; init; }
    public bool IsError { get; init; }
    public T? Data { get; init; }
}

public static class ApiResponse
{
    public static ApiResponse<T> Success<T>(T data)
    {
        return new ApiResponse<T>
        {
            Message = null,
            IsError = false,
            Data = data
        };
    }

    public static ApiResponse<object?> Error(string message, object? data = null)
    {
        return new ApiResponse<object?>
        {
            Message = message,
            IsError = true,
            Data = data
        };
    }
}
