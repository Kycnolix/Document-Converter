using DocumentConverter.Worker.Options;
using DocumentConverter.Worker.Utilities;
using DocumentConverter.Shared.Models;
using DocumentConverter.Shared.Storage;
using Microsoft.Extensions.Options;

namespace DocumentConverter.Worker.Services;

public sealed class InputFolderProcessor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx",
        ".xlsx",
        ".pptx",
        ".doc",
        ".xls",
        ".ppt",
        ".odt",
        ".ods",
        ".odp"
    };

    private readonly ILogger<InputFolderProcessor> _logger;
    private readonly ConverterOptions _options;
    private readonly LibreOfficeConverter _libreOfficeConverter;
    private readonly ConversionJobMetadataStore _metadataStore;

    public InputFolderProcessor(
        ILogger<InputFolderProcessor> logger,
        IOptions<ConverterOptions> options,
        LibreOfficeConverter libreOfficeConverter,
        ConversionJobMetadataStore metadataStore)
    {
        _logger = logger;
        _options = options.Value;
        _libreOfficeConverter = libreOfficeConverter;
        _metadataStore = metadataStore;
    }

    public async Task ProcessAsync(CancellationToken stoppingToken)
    {
        var files = Directory
            .EnumerateFiles(_options.InputRoot)
            .Where(IsSupportedFile)
            .OrderBy(File.GetCreationTimeUtc)
            .ToList();

        if (files.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {FileCount} input file(s).", files.Count);

        foreach (var sourcePath in files)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var trackedJobMetadata = await _metadataStore.TryLoadBySourceFileNameAsync(
                Path.GetFileName(sourcePath),
                stoppingToken);

            try
            {
                var targetPath = GetTargetPath(sourcePath);

                if (File.Exists(targetPath))
                {
                    _logger.LogInformation(
                        "Output already exists. Moving source to processed. Source={SourceFileName}, Output={OutputFileName}",
                        Path.GetFileName(sourcePath),
                        Path.GetFileName(targetPath));

                    if (trackedJobMetadata is not null)
                    {
                        await MarkReadyAsync(trackedJobMetadata, stoppingToken);
                    }

                    MoveToProcessed(sourcePath);
                    continue;
                }

                if (trackedJobMetadata is not null)
                {
                    await MarkProcessingAsync(trackedJobMetadata, stoppingToken);
                }

                var result = await _libreOfficeConverter.ConvertToPdfAsync(sourcePath, targetPath, stoppingToken);

                if (result.Success)
                {
                    if (trackedJobMetadata is not null)
                    {
                        await MarkReadyAsync(trackedJobMetadata, stoppingToken);
                    }

                    _logger.LogInformation(
                        "Conversion succeeded. Source={SourceFileName}, Output={TargetFileName}",
                        Path.GetFileName(sourcePath),
                        Path.GetFileName(targetPath));

                    MoveToProcessed(sourcePath);
                    continue;
                }

                if (trackedJobMetadata is not null)
                {
                    await MarkFailedAsync(trackedJobMetadata, result.ErrorCode, result.ErrorMessage, stoppingToken);
                }

                MoveToFailed(sourcePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed while processing source file. Source={SourceFileName}", Path.GetFileName(sourcePath));

                if (trackedJobMetadata is not null)
                {
                    await TryMarkFailedAfterUnexpectedExceptionAsync(trackedJobMetadata, stoppingToken);
                }

                MoveToFailed(sourcePath);
            }
        }
    }

    private bool IsSupportedFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension);
    }

    private string GetTargetPath(string sourcePath)
    {
        var targetFileName = Path.GetFileNameWithoutExtension(sourcePath) + ".pdf";
        return Path.Combine(_options.OutputRoot, targetFileName);
    }

    private void MoveToProcessed(string sourcePath)
    {
        FileMoveHelper.MoveToDirectory(_logger, sourcePath, _options.ProcessedRoot, "processed");
    }

    private void MoveToFailed(string sourcePath)
    {
        FileMoveHelper.MoveToDirectory(_logger, sourcePath, _options.FailedRoot, "failed");
    }

    private async Task MarkProcessingAsync(ConversionJobMetadata metadata, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        metadata.Status = ConversionJobStatus.Processing;
        metadata.StartedAtUtc ??= now;
        metadata.FinishedAtUtc = null;
        metadata.UpdatedAtUtc = now;
        metadata.AttemptCount += 1;
        metadata.ErrorCode = null;
        metadata.ErrorMessage = null;

        await _metadataStore.SaveAsync(metadata, cancellationToken);
    }

    private async Task MarkReadyAsync(ConversionJobMetadata metadata, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        metadata.Status = ConversionJobStatus.Ready;
        metadata.FinishedAtUtc = now;
        metadata.UpdatedAtUtc = now;
        metadata.ErrorCode = null;
        metadata.ErrorMessage = null;

        await _metadataStore.SaveAsync(metadata, cancellationToken);
    }

    private async Task MarkFailedAsync(
        ConversionJobMetadata metadata,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        metadata.Status = ConversionJobStatus.Failed;
        metadata.FinishedAtUtc = now;
        metadata.UpdatedAtUtc = now;
        metadata.ErrorCode = errorCode;
        metadata.ErrorMessage = errorMessage;

        await _metadataStore.SaveAsync(metadata, cancellationToken);
    }

    private async Task TryMarkFailedAfterUnexpectedExceptionAsync(
        ConversionJobMetadata metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            await MarkFailedAsync(
                metadata,
                "CONVERSION_EXCEPTION",
                "Conversion failed because an unexpected error occurred.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update tracked job metadata after unexpected error. JobId={JobId}", metadata.JobId);
        }
    }
}
