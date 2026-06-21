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
}
