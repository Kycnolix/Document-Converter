using DocumentConverter.Api.Models;
using DocumentConverter.Api.Options;
using DocumentConverter.Shared.Models;
using DocumentConverter.Shared.Storage;
using Microsoft.Extensions.Options;

namespace DocumentConverter.Api.Services;

public sealed class ConversionJobService
{
    private static readonly HashSet<string> SupportedSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
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

    private readonly ILogger<ConversionJobService> _logger;
    private readonly ConverterStorageOptions _options;
    private readonly IConversionJobStore _jobStore;

    public ConversionJobService(
        ILogger<ConversionJobService> logger,
        IOptions<ConverterStorageOptions> options,
        IConversionJobStore jobStore)
    {
        _logger = logger;
        _options = options.Value;
        _jobStore = jobStore;
    }

    public long MaxUploadBytes => _options.MaxUploadBytes;

    public void EnsureDirectories()
    {
        foreach (var directoryPath in _options.GetManagedDirectories())
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    public bool IsSupportedSourceExtension(string extension)
    {
        return SupportedSourceExtensions.Contains(extension);
    }

    public async Task<ConversionJobMetadata> CreateJobAsync(
        IFormFile file,
        string targetFormat,
        CancellationToken cancellationToken)
    {
        EnsureDirectories();

        var safeOriginalFileName = GetSafeOriginalFileName(file.FileName);
        var extension = Path.GetExtension(safeOriginalFileName).ToLowerInvariant();
        var jobId = Guid.NewGuid().ToString("D");
        var storedSourceFileName = $"{jobId}{extension}";
        var expectedOutputFileName = $"{jobId}.pdf";
        var sourcePath = Path.Combine(_options.InputRoot, storedSourceFileName);
        var tempSourcePath = Path.Combine(_options.InputRoot, $"{jobId}{extension}.uploading");
        var now = DateTimeOffset.UtcNow;

        var metadata = new ConversionJobMetadata
        {
            JobId = jobId,
            OriginalFileName = safeOriginalFileName,
            StoredSourceFileName = storedSourceFileName,
            ExpectedOutputFileName = expectedOutputFileName,
            TargetFormat = targetFormat,
            Status = ConversionJobStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            AttemptCount = 0,
            SourceExtension = extension,
            SourceSizeBytes = file.Length
        };

        try
        {
            await using (var sourceStream = File.Create(tempSourcePath))
            {
                await file.CopyToAsync(sourceStream, cancellationToken);
            }

            await _jobStore.CreateAsync(metadata, cancellationToken);
            File.Move(tempSourcePath, sourcePath, overwrite: false);

            _logger.LogInformation(
                "Created conversion job {JobId}. Source={StoredSourceFileName}, Original={OriginalFileName}",
                metadata.JobId,
                metadata.StoredSourceFileName,
                metadata.OriginalFileName);

            return metadata;
        }
        catch
        {
            TryDeleteFile(tempSourcePath);
            TryDeleteFile(sourcePath);

            try
            {
                await _jobStore.DeleteAsync(jobId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete MongoDB job after upload cleanup. JobId={JobId}", jobId);
            }

            throw;
        }
    }

    public async Task<ConversionJobMetadata?> GetMetadataAsync(string jobId, CancellationToken cancellationToken)
    {
        return await _jobStore.GetByJobIdAsync(jobId, cancellationToken);
    }

    public ConversionStatusResponse GetStatus(ConversionJobMetadata metadata)
    {
        var resolvedStatus = ResolveStatus(metadata);
        var resultUrl = resolvedStatus == ConversionJobStatus.Ready
            ? $"/api/conversions/{metadata.JobId}/result"
            : null;

        return new ConversionStatusResponse
        {
            JobId = metadata.JobId,
            Status = resolvedStatus,
            OriginalFileName = metadata.OriginalFileName,
            ResultUrl = resultUrl,
            ErrorCode = metadata.ErrorCode,
            ErrorMessage = metadata.ErrorMessage,
            CreatedAtUtc = metadata.CreatedAtUtc,
            StartedAtUtc = metadata.StartedAtUtc,
            FinishedAtUtc = metadata.FinishedAtUtc,
            UpdatedAtUtc = metadata.UpdatedAtUtc
        };
    }

    public string GetOutputFilePath(ConversionJobMetadata metadata)
    {
        return Path.Combine(_options.OutputRoot, metadata.ExpectedOutputFileName);
    }

    public string GetResultDownloadFileName(ConversionJobMetadata metadata)
    {
        return Path.GetFileNameWithoutExtension(metadata.OriginalFileName) + ".pdf";
    }

    private string ResolveStatus(ConversionJobMetadata metadata)
    {
        if (File.Exists(GetOutputFilePath(metadata)))
        {
            return ConversionJobStatus.Ready;
        }

        if (string.Equals(metadata.Status, ConversionJobStatus.Ready, StringComparison.OrdinalIgnoreCase))
        {
            return ConversionJobStatus.Unknown;
        }

        if (string.Equals(metadata.Status, ConversionJobStatus.Failed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(metadata.Status, ConversionJobStatus.Unsupported, StringComparison.OrdinalIgnoreCase))
        {
            return metadata.Status;
        }

        if (string.Equals(metadata.Status, ConversionJobStatus.Processing, StringComparison.OrdinalIgnoreCase))
        {
            return ConversionJobStatus.Processing;
        }

        if (File.Exists(Path.Combine(_options.InputRoot, metadata.StoredSourceFileName))
            && string.Equals(metadata.Status, ConversionJobStatus.Pending, StringComparison.OrdinalIgnoreCase))
        {
            return ConversionJobStatus.Pending;
        }

        if (File.Exists(Path.Combine(_options.OutputRoot, metadata.ExpectedOutputFileName)))
        {
            return ConversionJobStatus.Ready;
        }

        if (File.Exists(Path.Combine(_options.FailedRoot, metadata.StoredSourceFileName)))
        {
            return ConversionJobStatus.Failed;
        }

        if (File.Exists(Path.Combine(_options.ProcessedRoot, metadata.StoredSourceFileName)))
        {
            return ConversionJobStatus.Failed;
        }

        return ConversionJobStatus.Unknown;
    }

    private static string GetSafeOriginalFileName(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);

        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return "upload";
        }

        return safeFileName;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up temporary file: {Path}", path);
        }
    }
}
