using System.Text.Json;
using DocumentConverter.Api.Models;
using DocumentConverter.Api.Options;
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
    private readonly JsonSerializerOptions _jsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public ConversionJobService(
        ILogger<ConversionJobService> logger,
        IOptions<ConverterStorageOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public long MaxUploadBytes => _options.MaxUploadBytes;

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(_options.InputRoot);
        Directory.CreateDirectory(_options.OutputRoot);
        Directory.CreateDirectory(_options.ProcessedRoot);
        Directory.CreateDirectory(_options.FailedRoot);
        Directory.CreateDirectory(_options.TempRoot);
        Directory.CreateDirectory(_options.JobsRoot);
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
        var jobId = CreateUniqueJobId(extension);
        var storedSourceFileName = $"{jobId}{extension}";
        var expectedOutputFileName = $"{jobId}.pdf";
        var sourcePath = Path.Combine(_options.InputRoot, storedSourceFileName);
        var metadataPath = GetMetadataPath(jobId);

        var metadata = new ConversionJobMetadata
        {
            JobId = jobId,
            OriginalFileName = safeOriginalFileName,
            StoredSourceFileName = storedSourceFileName,
            ExpectedOutputFileName = expectedOutputFileName,
            TargetFormat = targetFormat,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            await using (var sourceStream = File.Create(sourcePath))
            {
                await file.CopyToAsync(sourceStream, cancellationToken);
            }

            await using var metadataStream = File.Create(metadataPath);
            await JsonSerializer.SerializeAsync(metadataStream, metadata, _jsonSerializerOptions, cancellationToken);

            _logger.LogInformation(
                "Created conversion job {JobId}. Source={StoredSourceFileName}, Original={OriginalFileName}",
                metadata.JobId,
                metadata.StoredSourceFileName,
                metadata.OriginalFileName);

            return metadata;
        }
        catch
        {
            TryDeleteFile(sourcePath);
            TryDeleteFile(metadataPath);
            throw;
        }
    }

    public async Task<ConversionJobMetadata?> GetMetadataAsync(string jobId, CancellationToken cancellationToken)
    {
        var metadataPath = GetMetadataPath(jobId);

        if (!File.Exists(metadataPath))
        {
            return null;
        }

        await using var metadataStream = File.OpenRead(metadataPath);
        return await JsonSerializer.DeserializeAsync<ConversionJobMetadata>(
            metadataStream,
            _jsonSerializerOptions,
            cancellationToken);
    }

    public ConversionStatusResponse GetStatus(ConversionJobMetadata metadata)
    {
        var resultUrl = $"/api/conversions/{metadata.JobId}/result";

        if (File.Exists(GetOutputFilePath(metadata)))
        {
            return new ConversionStatusResponse
            {
                JobId = metadata.JobId,
                Status = "Ready",
                OriginalFileName = metadata.OriginalFileName,
                ResultUrl = resultUrl
            };
        }

        if (File.Exists(Path.Combine(_options.FailedRoot, metadata.StoredSourceFileName)))
        {
            return new ConversionStatusResponse
            {
                JobId = metadata.JobId,
                Status = "Failed",
                OriginalFileName = metadata.OriginalFileName,
                ResultUrl = null
            };
        }

        if (File.Exists(Path.Combine(_options.InputRoot, metadata.StoredSourceFileName)))
        {
            return new ConversionStatusResponse
            {
                JobId = metadata.JobId,
                Status = "Pending",
                OriginalFileName = metadata.OriginalFileName,
                ResultUrl = null
            };
        }

        if (File.Exists(Path.Combine(_options.ProcessedRoot, metadata.StoredSourceFileName)))
        {
            return new ConversionStatusResponse
            {
                JobId = metadata.JobId,
                Status = "Failed",
                OriginalFileName = metadata.OriginalFileName,
                ResultUrl = null
            };
        }

        return new ConversionStatusResponse
        {
            JobId = metadata.JobId,
            Status = "Unknown",
            OriginalFileName = metadata.OriginalFileName,
            ResultUrl = null
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

    public static bool TryNormalizeJobId(string jobId, out string normalizedJobId)
    {
        if (Guid.TryParse(jobId, out var parsedJobId))
        {
            normalizedJobId = parsedJobId.ToString("D");
            return true;
        }

        normalizedJobId = string.Empty;
        return false;
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
