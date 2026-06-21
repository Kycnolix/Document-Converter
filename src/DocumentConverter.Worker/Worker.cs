using DocumentConverter.Worker.Options;
using DocumentConverter.Worker.Services;
using DocumentConverter.Shared.Options;
using Microsoft.Extensions.Options;

namespace DocumentConverter.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ConverterOptions _options;
    private readonly MongoOptions _mongoOptions;
    private readonly InputFolderProcessor _inputFolderProcessor;
    private readonly LibreOfficeConverter _libreOfficeConverter;
    private readonly string _effectiveWorkerId;

    public Worker(
        ILogger<Worker> logger,
        IHostEnvironment hostEnvironment,
        IOptions<ConverterOptions> options,
        IOptions<MongoOptions> mongoOptions,
        InputFolderProcessor inputFolderProcessor,
        LibreOfficeConverter libreOfficeConverter)
    {
        _logger = logger;
        _hostEnvironment = hostEnvironment;
        _options = options.Value;
        _mongoOptions = mongoOptions.Value;
        _inputFolderProcessor = inputFolderProcessor;
        _libreOfficeConverter = libreOfficeConverter;
        _effectiveWorkerId = ConverterOptions.ResolveWorkerId(_options.WorkerId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureDirectories();

        _logger.LogInformation("document-converter worker started at {Time}", DateTimeOffset.Now);
        _logger.LogInformation("Environment: {Environment}", _hostEnvironment.EnvironmentName);
        _logger.LogInformation("InputRoot: {InputRoot}", _options.InputRoot);
        _logger.LogInformation("OutputRoot: {OutputRoot}", _options.OutputRoot);
        _logger.LogInformation("ProcessedRoot: {ProcessedRoot}", _options.ProcessedRoot);
        _logger.LogInformation("FailedRoot: {FailedRoot}", _options.FailedRoot);
        _logger.LogInformation("TempRoot: {TempRoot}", _options.TempRoot);
        _logger.LogInformation("JobsRoot: {JobsRoot}", _options.JobsRoot);
        _logger.LogInformation("HeartbeatFilePath: {HeartbeatFilePath}", _options.HeartbeatFilePath);
        _logger.LogInformation("MongoDatabaseName: {MongoDatabaseName}", _mongoOptions.DatabaseName);
        _logger.LogInformation("MongoCollectionName: {MongoCollectionName}", _mongoOptions.ConversionJobsCollectionName);
        _logger.LogInformation("WorkerId: {WorkerId}", _effectiveWorkerId);
        _logger.LogInformation("ConversionTimeoutSeconds: {ConversionTimeoutSeconds}", _options.ConversionTimeoutSeconds);
        _logger.LogInformation("JobLockSeconds: {JobLockSeconds}", _options.JobLockSeconds);
        _logger.LogInformation("PollingIntervalSeconds: {PollingIntervalSeconds}", _options.PollingIntervalSeconds);

        await _libreOfficeConverter.LogVersionAsync(stoppingToken);
        await WriteHeartbeatAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _inputFolderProcessor.ProcessAsync(stoppingToken);
                await WriteHeartbeatAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void EnsureDirectories()
    {
        foreach (var directoryPath in _options.GetManagedDirectories())
        {
            Directory.CreateDirectory(directoryPath);
        }

        var heartbeatDirectoryPath = Path.GetDirectoryName(_options.HeartbeatFilePath);

        if (!string.IsNullOrWhiteSpace(heartbeatDirectoryPath))
        {
            Directory.CreateDirectory(heartbeatDirectoryPath);
        }
    }

    private async Task WriteHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllTextAsync(
                _options.HeartbeatFilePath,
                DateTimeOffset.UtcNow.ToString("O"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update worker heartbeat file at {HeartbeatFilePath}", _options.HeartbeatFilePath);
        }
    }
}
