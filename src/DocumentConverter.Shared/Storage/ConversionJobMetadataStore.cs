using System.Text.Json;
using DocumentConverter.Shared.Models;
using Microsoft.Extensions.Logging;

namespace DocumentConverter.Shared.Storage;

public sealed class ConversionJobMetadataStore
{
    private readonly ILogger<ConversionJobMetadataStore> _logger;
    private readonly string _jobsRoot;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public ConversionJobMetadataStore(ILogger<ConversionJobMetadataStore> logger, string jobsRoot)
    {
        _logger = logger;
        _jobsRoot = jobsRoot;
    }

    public void EnsureDirectory()
    {
        Directory.CreateDirectory(_jobsRoot);
    }

    public async Task<ConversionJobMetadata?> LoadAsync(string jobId, CancellationToken cancellationToken)
    {
        if (!TryNormalizeJobId(jobId, out var normalizedJobId))
        {
            return null;
        }

        var metadataPath = GetMetadataPath(normalizedJobId);

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

    public async Task<ConversionJobMetadata?> TryLoadBySourceFileNameAsync(
        string sourceFileName,
        CancellationToken cancellationToken)
    {
        var safeFileName = Path.GetFileName(sourceFileName);
        var jobIdCandidate = Path.GetFileNameWithoutExtension(safeFileName);

        if (!TryNormalizeJobId(jobIdCandidate, out var normalizedJobId))
        {
            return null;
        }

        var metadata = await LoadAsync(normalizedJobId, cancellationToken);

        if (metadata is null)
        {
            return null;
        }

        if (!string.Equals(metadata.StoredSourceFileName, safeFileName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return metadata;
    }

    public async Task SaveAsync(ConversionJobMetadata metadata, CancellationToken cancellationToken)
    {
        if (!TryNormalizeJobId(metadata.JobId, out var normalizedJobId))
        {
            throw new InvalidOperationException("Metadata jobId must be a valid GUID.");
        }

        EnsureDirectory();

        metadata.JobId = normalizedJobId;

        var metadataPath = GetMetadataPath(normalizedJobId);
        var tempFilePath = Path.Combine(_jobsRoot, $"{normalizedJobId}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var metadataStream = new FileStream(
                tempFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(metadataStream, metadata, _jsonSerializerOptions, cancellationToken);
            }

            File.Move(tempFilePath, metadataPath, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempFilePath);
            throw;
        }
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

    private string GetMetadataPath(string jobId)
    {
        return Path.Combine(_jobsRoot, $"{jobId}.json");
    }

    private void TryDeleteTempFile(string path)
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
            _logger.LogWarning(ex, "Failed to delete temporary metadata file: {Path}", path);
        }
    }
}
