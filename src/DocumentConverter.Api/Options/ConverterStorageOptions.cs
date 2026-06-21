namespace DocumentConverter.Api.Options;

public sealed class ConverterStorageOptions
{
    public string InputRoot { get; set; } = "/app/data/input";
    public string OutputRoot { get; set; } = "/app/data/output";
    public string ProcessedRoot { get; set; } = "/app/data/processed";
    public string FailedRoot { get; set; } = "/app/data/failed";
    public string TempRoot { get; set; } = "/app/data/temp";
    public string JobsRoot { get; set; } = "/app/data/jobs";
    public long MaxUploadBytes { get; set; } = 52_428_800;

    public IReadOnlyList<string> GetManagedDirectories()
    {
        return
        [
            InputRoot,
            OutputRoot,
            ProcessedRoot,
            FailedRoot,
            TempRoot,
            JobsRoot
        ];
    }
}
