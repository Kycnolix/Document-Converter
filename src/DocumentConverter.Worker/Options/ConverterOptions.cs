namespace DocumentConverter.Worker.Options;

public sealed class ConverterOptions
{
    public string InputRoot { get; set; } = "/app/data/input";
    public string OutputRoot { get; set; } = "/app/data/output";
    public string ProcessedRoot { get; set; } = "/app/data/processed";
    public string FailedRoot { get; set; } = "/app/data/failed";
    public string TempRoot { get; set; } = "/app/data/temp";
    public string JobsRoot { get; set; } = "/app/data/jobs";
    public string LibreOfficePath { get; set; } = "soffice";
    public int ConversionTimeoutSeconds { get; set; } = 120;
    public int PollingIntervalSeconds { get; set; } = 30;
    public string WorkerId { get; set; } = string.Empty;
    public int JobLockSeconds { get; set; } = 300;
    public string HeartbeatFilePath { get; set; } = "/app/data/temp/worker-heartbeat.txt";

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

    public static string ResolveWorkerId(string? configuredWorkerId)
    {
        if (!string.IsNullOrWhiteSpace(configuredWorkerId))
        {
            return configuredWorkerId;
        }

        if (!string.IsNullOrWhiteSpace(Environment.MachineName))
        {
            return Environment.MachineName;
        }

        return "worker-" + Guid.NewGuid().ToString("N");
    }
}
