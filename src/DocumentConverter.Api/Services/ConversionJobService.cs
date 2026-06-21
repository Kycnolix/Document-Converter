using DocumentConverter.Api.Models;
using DocumentConverter.Api.Options;
using DocumentConverter.Shared.Models;
using DocumentConverter.Shared.Storage;
using Microsoft.Extensions.Options;
using SharedConversionJobMetadata = DocumentConverter.Shared.Models.ConversionJobMetadata;

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
    private readonly ConversionJobMetadataStore _metadataStore;

    public ConversionJobService(
        ILogger<ConversionJobService> logger,
        IOptions<ConverterStorageOptions> options,
        ConversionJobMetadataStore metadataStore)
    {
        _logger = logger;
        _options = options.Value;
        _metadataStore = metadataStore;
    }

    public long MaxUploadBytes => _options.MaxUploadBytes;

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(_options.InputRoot);
        Directory.CreateDirectory(_options.OutputRoot);
        Directory.CreateDirectory(_options.ProcessedRoot);
        Directory.CreateDirectory(_options.FailedRoot);
        Directory.CreateDirectory(_options.TempRoot);
        _metadataStore.EnsureDirectory();
    }

    public bool IsSupportedSourceExtension(string extension)
    {
        return SupportedSourceExtensions.Contains(extension);
    }

    public async Task<SharedConversionJobMetadata> CreateJobAsync(
        IFormFile file,
        string targetFormat,
        CancellationToken cancellationToken)
    {
        EnsureDirectories();

        var safeOriginalFileName = GetSafeOriginalFileName(file.FileName);
        var extension = Path.GetExtension(safeOriginalFileName).ToLowerInvariant();
        var jobId = CreateUniqueJobId(extension);
        var storedSourceFileName = $"{jobId}{extension}";
        var expectedOutputFileName = $"{jobId}.pdf";
        var sourcePath = Path.Combine(_options.InputRoot, storedSourceFileName);
        var tempSourcePath = Path.Combine(_options.TempRoot, $"{jobId}{extension}.uploading");
        var now = DateTimeOffset.UtcNow;

        var metadata = new SharedConversionJobMetadata
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

            await _metadataStore.SaveAsync(metadata, cancellationToken);
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
            TryDeleteFile(GetMetadataPath(jobId));
            throw;
        }
    }

    public async Task<SharedConversionJobMetadata?> GetMetadataAsync(string jobId, CancellationToken cancellationToken)
    {
        return await _metadataStore.LoadAsync(jobId, cancellationToken);
    }

    public ConversionStatusResponse GetStatus(SharedConversionJobMetadata metadata)
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

    public string GetOutputFilePath(SharedConversionJobMetadata metadata)
    {
        return Path.Combine(_options.OutputRoot, metadata.ExpectedOutputFileName);
    }

    public string GetResultDownloadFileName(SharedConversionJobMetadata metadata)
    {
        return Path.GetFileNameWithoutExtension(metadata.OriginalFileName) + ".pdf";
    }

    private string CreateUniqueJobId(string extension)
    {
        while (true)
        {
            var jobId = Guid.NewGuid().ToString("D");

            if (IsJobIdAvailable(jobId, extension))
            {
                return jobId;
            }
        }
    }

    private bool IsJobIdAvailable(string jobId, string extension)
    {
        var storedSourceFileName = $"{jobId}{extension}";
        var expectedOutputFileName = $"{jobId}.pdf";

        return !File.Exists(GetMetadataPath(jobId))
            && !File.Exists(Path.Combine(_options.InputRoot, storedSourceFileName))
            && !File.Exists(Path.Combine(_options.OutputRoot, expectedOutputFileName))
            && !File.Exists(Path.Combine(_options.ProcessedRoot, storedSourceFileName))
            && !File.Exists(Path.Combine(_options.FailedRoot, storedSourceFileName));
    }

    private string GetMetadataPath(string jobId)
    {
        return Path.Combine(_options.JobsRoot, $"{jobId}.json");
    }

    private string ResolveStatus(SharedConversionJobMetadata metadata)
    {
        if (File.Exists(GetOutputFilePath(metadata)))
        {
            return ConversionJobStatus.Ready;
        }

        if (string.Equals(metadata.Status, ConversionJobStatus.Failed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(metadata.Status, ConversionJobStatus.Unsupported, StringComparison.OrdinalIgnoreCase))
        {
            return ConversionJobStatus.Failed;
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
