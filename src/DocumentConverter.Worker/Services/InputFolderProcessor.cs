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
    private readonly IConversionJobStore _jobStore;
    private readonly string _workerId;
    private readonly TimeSpan _jobLockDuration;
    private const string SourceFileNotFoundErrorCode = "SOURCE_FILE_NOT_FOUND";
    private const string UnsupportedFileTypeErrorCode = "UNSUPPORTED_FILE_TYPE";

    public InputFolderProcessor(
        ILogger<InputFolderProcessor> logger,
        IOptions<ConverterOptions> options,
        LibreOfficeConverter libreOfficeConverter,
        IConversionJobStore jobStore)
    {
        _logger = logger;
        _options = options.Value;
        _libreOfficeConverter = libreOfficeConverter;
        _jobStore = jobStore;
        _workerId = string.IsNullOrWhiteSpace(_options.WorkerId)
            ? CreateDefaultWorkerId()
            : _options.WorkerId;
        _jobLockDuration = TimeSpan.FromSeconds(_options.JobLockSeconds > 0 ? _options.JobLockSeconds : 300);
    }

    public async Task ProcessAsync(CancellationToken stoppingToken)
    {
        var processedTrackedJob = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimedJob = await TryClaimNextPendingJobAsync(stoppingToken);

            if (claimedJob is null)
            {
                break;
            }

            processedTrackedJob = true;
            await ProcessTrackedJobAsync(claimedJob, stoppingToken);
        }

        if (!processedTrackedJob)
        {
            await ProcessLegacyFilesAsync(stoppingToken);
        }
    }

    private async Task ProcessLegacyFilesAsync(CancellationToken stoppingToken)
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

            if (await IsTrackedJobSourceFileAsync(Path.GetFileName(sourcePath), stoppingToken))
            {
                continue;
            }

            await ConvertLegacyFileAsync(sourcePath, stoppingToken);
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

    private async Task<bool> IsTrackedJobSourceFileAsync(string sourceFileName, CancellationToken cancellationToken)
    {
        try
        {
            var trackedJob = await _jobStore.GetByStoredSourceFileNameAsync(sourceFileName, cancellationToken);
            return trackedJob is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB lookup failed while checking tracked source file. Source={SourceFileName}", sourceFileName);
            return false;
        }
    }

    private async Task<ConversionJobMetadata?> TryClaimNextPendingJobAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _jobStore.ClaimNextPendingAsync(_workerId, _jobLockDuration, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB job claim failed. Falling back to legacy folder scan.");
            return null;
        }
    }

    private async Task ProcessTrackedJobAsync(ConversionJobMetadata job, CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(_options.InputRoot, job.StoredSourceFileName);
        var targetPath = Path.Combine(_options.OutputRoot, job.ExpectedOutputFileName);

        if (!File.Exists(sourcePath))
        {
            if (File.Exists(targetPath))
            {
                await MarkTrackedJobReadyAsync(job.JobId, cancellationToken);
                return;
            }

            _logger.LogError("Tracked job source file was not found. JobId={JobId}, SourcePath={SourcePath}", job.JobId, sourcePath);
            await MarkTrackedJobFailedAsync(job.JobId, SourceFileNotFoundErrorCode, "Tracked source file was not found.", cancellationToken);
            return;
        }

        if (!IsSupportedFile(sourcePath))
        {
            _logger.LogError("Tracked job source file type is not supported. JobId={JobId}, SourceFileName={SourceFileName}", job.JobId, job.StoredSourceFileName);
            await MarkTrackedJobUnsupportedAsync(job.JobId, UnsupportedFileTypeErrorCode, "Tracked source file type is not supported.", cancellationToken);
            MoveToFailed(sourcePath);
            return;
        }

        if (File.Exists(targetPath))
        {
            _logger.LogInformation(
                "Output already exists. Moving source to processed. Source={SourceFileName}, Output={OutputFileName}",
                Path.GetFileName(sourcePath),
                Path.GetFileName(targetPath));

            await MarkTrackedJobReadyAsync(job.JobId, cancellationToken);
            MoveToProcessed(sourcePath);
            return;
        }

        var result = await _libreOfficeConverter.ConvertToPdfAsync(sourcePath, targetPath, cancellationToken);

        if (result.Success)
        {
            await MarkTrackedJobReadyAsync(job.JobId, cancellationToken);

            _logger.LogInformation(
                "Conversion succeeded. Source={SourceFileName}, Output={TargetFileName}",
                Path.GetFileName(sourcePath),
                Path.GetFileName(targetPath));

            MoveToProcessed(sourcePath);
            return;
        }

        await MarkTrackedJobFailedAsync(
            job.JobId,
            result.ErrorCode ?? "CONVERSION_EXCEPTION",
            result.ErrorMessage ?? "Conversion failed because an unexpected error occurred.",
            cancellationToken);

        MoveToFailed(sourcePath);
    }

    private async Task ConvertLegacyFileAsync(string sourcePath, CancellationToken cancellationToken)
    {
        try
        {
            var targetPath = GetTargetPath(sourcePath);

            if (File.Exists(targetPath))
            {
                _logger.LogInformation(
                    "Output already exists. Moving source to processed. Source={SourceFileName}, Output={OutputFileName}",
                    Path.GetFileName(sourcePath),
                    Path.GetFileName(targetPath));

                MoveToProcessed(sourcePath);
                return;
            }

            var result = await _libreOfficeConverter.ConvertToPdfAsync(sourcePath, targetPath, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Conversion succeeded. Source={SourceFileName}, Output={TargetFileName}",
                    Path.GetFileName(sourcePath),
                    Path.GetFileName(targetPath));

                MoveToProcessed(sourcePath);
                return;
            }

            MoveToFailed(sourcePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed while processing legacy source file. Source={SourceFileName}", Path.GetFileName(sourcePath));
            MoveToFailed(sourcePath);
        }
    }

    private async Task MarkTrackedJobReadyAsync(string jobId, CancellationToken cancellationToken)
    {
        await ExecuteStoreUpdateAsync(
            () => _jobStore.MarkReadyAsync(jobId, cancellationToken),
            "Failed to mark tracked job as Ready. JobId={JobId}",
            jobId);
    }

    private async Task MarkTrackedJobFailedAsync(
        string jobId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await ExecuteStoreUpdateAsync(
            () => _jobStore.MarkFailedAsync(jobId, errorCode, errorMessage, cancellationToken),
            "Failed to mark tracked job as Failed. JobId={JobId}",
            jobId);
    }

    private async Task MarkTrackedJobUnsupportedAsync(
        string jobId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await ExecuteStoreUpdateAsync(
            () => _jobStore.MarkUnsupportedAsync(jobId, errorCode, errorMessage, cancellationToken),
            "Failed to mark tracked job as Unsupported. JobId={JobId}",
            jobId);
    }

    private async Task ExecuteStoreUpdateAsync(Func<Task> updateAction, string logMessage, string jobId)
    {
        try
        {
            await updateAction();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, logMessage, jobId);
        }
    }

    private static string CreateDefaultWorkerId()
    {
        if (!string.IsNullOrWhiteSpace(Environment.MachineName))
        {
            return Environment.MachineName;
        }

        return "worker-" + Guid.NewGuid().ToString("N");
    }
}
