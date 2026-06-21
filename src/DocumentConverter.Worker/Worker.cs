using DocumentConverter.Worker.Options;
using DocumentConverter.Worker.Services;
using Microsoft.Extensions.Options;

namespace DocumentConverter.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ConverterOptions _options;
    private readonly InputFolderProcessor _inputFolderProcessor;
    private readonly LibreOfficeConverter _libreOfficeConverter;

    public Worker(
        ILogger<Worker> logger,
        IOptions<ConverterOptions> options,
        InputFolderProcessor inputFolderProcessor,
        LibreOfficeConverter libreOfficeConverter)
    {
        _logger = logger;
        _options = options.Value;
        _inputFolderProcessor = inputFolderProcessor;
        _libreOfficeConverter = libreOfficeConverter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureDirectories();

        _logger.LogInformation("document-converter worker started at {Time}", DateTimeOffset.Now);
        _logger.LogInformation("InputRoot: {InputRoot}", _options.InputRoot);
        _logger.LogInformation("OutputRoot: {OutputRoot}", _options.OutputRoot);
        _logger.LogInformation("ProcessedRoot: {ProcessedRoot}", _options.ProcessedRoot);
        _logger.LogInformation("FailedRoot: {FailedRoot}", _options.FailedRoot);
        _logger.LogInformation("TempRoot: {TempRoot}", _options.TempRoot);
        _logger.LogInformation("PollingIntervalSeconds: {PollingIntervalSeconds}", _options.PollingIntervalSeconds);

        await _libreOfficeConverter.LogVersionAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _inputFolderProcessor.ProcessAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_options.InputRoot);
        Directory.CreateDirectory(_options.OutputRoot);
        Directory.CreateDirectory(_options.ProcessedRoot);
        Directory.CreateDirectory(_options.FailedRoot);
        Directory.CreateDirectory(_options.TempRoot);
    }
}
