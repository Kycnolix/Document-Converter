namespace DocumentConverter.Api.Models;

public sealed class ValidationErrorData
{
    public Dictionary<string, string[]> Errors { get; init; } = [];

    public static ValidationErrorData FromError(string key, string message)
    {
        return new ValidationErrorData
        {
            Errors = new Dictionary<string, string[]>
            {
                [key] = [message]
            }
        };
    }
}
