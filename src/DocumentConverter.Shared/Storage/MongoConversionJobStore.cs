using DocumentConverter.Shared.Models;
using DocumentConverter.Shared.Options;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DocumentConverter.Shared.Storage;

public sealed class MongoConversionJobStore : IConversionJobStore
{
    private readonly ILogger<MongoConversionJobStore> _logger;
    private readonly IMongoCollection<ConversionJobMetadata> _collection;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public MongoConversionJobStore(ILogger<MongoConversionJobStore> logger, MongoOptions options)
    {
        _logger = logger;

        var client = new MongoClient(options.ConnectionString);
        var database = client.GetDatabase(options.DatabaseName);
        _collection = database.GetCollection<ConversionJobMetadata>(options.ConversionJobsCollectionName);
    }

    public async Task CreateAsync(ConversionJobMetadata metadata, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        metadata.JobId = NormalizeJobIdOrThrow(metadata.JobId);
        await _collection.InsertOneAsync(metadata, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(string jobId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var normalizedJobId = NormalizeJobIdOrThrow(jobId);
        var filter = Builders<ConversionJobMetadata>.Filter.Eq(x => x.JobId, normalizedJobId);
        await _collection.DeleteOneAsync(filter, cancellationToken);
    }

    public async Task<ConversionJobMetadata?> GetByJobIdAsync(string jobId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (!TryNormalizeJobId(jobId, out var normalizedJobId))
        {
            return null;
        }

        var filter = Builders<ConversionJobMetadata>.Filter.Eq(x => x.JobId, normalizedJobId);
        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ConversionJobMetadata?> GetByStoredSourceFileNameAsync(
        string storedSourceFileName,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var safeFileName = Path.GetFileName(storedSourceFileName);
        var filter = Builders<ConversionJobMetadata>.Filter.Eq(x => x.StoredSourceFileName, safeFileName);
        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ConversionJobMetadata?> ClaimNextPendingAsync(
        string workerId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var lockUntil = now.Add(lockDuration);
        var updateForPending = Builders<ConversionJobMetadata>.Update
            .Set(x => x.Status, ConversionJobStatus.Processing)
            .Set(x => x.StartedAtUtc, now)
            .Set(x => x.UpdatedAtUtc, now)
            .Set(x => x.WorkerId, workerId)
            .Set(x => x.LockedUntilUtc, lockUntil)
            .Set(x => x.LastClaimedAtUtc, now)
            .Set(x => x.FinishedAtUtc, null)
            .Set(x => x.ErrorCode, null)
            .Set(x => x.ErrorMessage, null)
            .Inc(x => x.AttemptCount, 1);

        var options = new FindOneAndUpdateOptions<ConversionJobMetadata>
        {
            Sort = Builders<ConversionJobMetadata>.Sort.Ascending(x => x.CreatedAtUtc),
            ReturnDocument = ReturnDocument.After
        };

        var pendingFilter = Builders<ConversionJobMetadata>.Filter.Eq(x => x.Status, ConversionJobStatus.Pending);
        var claimedPendingJob = await _collection.FindOneAndUpdateAsync(
            pendingFilter,
            updateForPending,
            options,
            cancellationToken);

        if (claimedPendingJob is not null)
        {
            return claimedPendingJob;
        }

        var staleProcessingFilter = Builders<ConversionJobMetadata>.Filter.And(
            Builders<ConversionJobMetadata>.Filter.Eq(x => x.Status, ConversionJobStatus.Processing),
            Builders<ConversionJobMetadata>.Filter.Lt(x => x.LockedUntilUtc, now));

        var updateForStaleProcessing = Builders<ConversionJobMetadata>.Update
            .Set(x => x.UpdatedAtUtc, now)
            .Set(x => x.WorkerId, workerId)
            .Set(x => x.LockedUntilUtc, lockUntil)
            .Set(x => x.LastClaimedAtUtc, now)
            .Set(x => x.FinishedAtUtc, null)
            .Set(x => x.ErrorCode, null)
            .Set(x => x.ErrorMessage, null)
            .Inc(x => x.AttemptCount, 1);

        return await _collection.FindOneAndUpdateAsync(
            staleProcessingFilter,
            updateForStaleProcessing,
            options,
            cancellationToken);
    }

    public async Task MarkReadyAsync(string jobId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var normalizedJobId = NormalizeJobIdOrThrow(jobId);

        var filter = Builders<ConversionJobMetadata>.Filter.Eq(x => x.JobId, normalizedJobId);
        var update = Builders<ConversionJobMetadata>.Update
            .Set(x => x.Status, ConversionJobStatus.Ready)
            .Set(x => x.FinishedAtUtc, now)
            .Set(x => x.UpdatedAtUtc, now)
            .Set(x => x.LockedUntilUtc, null)
            .Set(x => x.ErrorCode, null)
            .Set(x => x.ErrorMessage, null);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    public async Task MarkFailedAsync(
        string jobId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var normalizedJobId = NormalizeJobIdOrThrow(jobId);

        var filter = Builders<ConversionJobMetadata>.Filter.Eq(x => x.JobId, normalizedJobId);
        var update = Builders<ConversionJobMetadata>.Update
            .Set(x => x.Status, ConversionJobStatus.Failed)
            .Set(x => x.FinishedAtUtc, now)
            .Set(x => x.UpdatedAtUtc, now)
            .Set(x => x.LockedUntilUtc, null)
            .Set(x => x.ErrorCode, errorCode)
            .Set(x => x.ErrorMessage, errorMessage);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    public async Task MarkUnsupportedAsync(
        string jobId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var normalizedJobId = NormalizeJobIdOrThrow(jobId);

        var filter = Builders<ConversionJobMetadata>.Filter.Eq(x => x.JobId, normalizedJobId);
        var update = Builders<ConversionJobMetadata>.Update
            .Set(x => x.Status, ConversionJobStatus.Unsupported)
            .Set(x => x.FinishedAtUtc, now)
            .Set(x => x.UpdatedAtUtc, now)
            .Set(x => x.LockedUntilUtc, null)
            .Set(x => x.ErrorCode, errorCode)
            .Set(x => x.ErrorMessage, errorMessage);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);

        try
        {
            if (_initialized)
            {
                return;
            }

            var indexModels = new[]
            {
                new CreateIndexModel<ConversionJobMetadata>(
                    Builders<ConversionJobMetadata>.IndexKeys.Ascending(x => x.JobId),
                    new CreateIndexOptions { Unique = true, Name = "ux_conversion_jobs_job_id" }),
                new CreateIndexModel<ConversionJobMetadata>(
                    Builders<ConversionJobMetadata>.IndexKeys.Ascending(x => x.StoredSourceFileName),
                    new CreateIndexOptions { Unique = true, Name = "ux_conversion_jobs_stored_source_file_name" }),
                new CreateIndexModel<ConversionJobMetadata>(
                    Builders<ConversionJobMetadata>.IndexKeys
                        .Ascending(x => x.Status)
                        .Ascending(x => x.CreatedAtUtc),
                    new CreateIndexOptions { Name = "ix_conversion_jobs_status_created_at_utc" }),
                new CreateIndexModel<ConversionJobMetadata>(
                    Builders<ConversionJobMetadata>.IndexKeys.Ascending(x => x.LockedUntilUtc),
                    new CreateIndexOptions { Name = "ix_conversion_jobs_locked_until_utc" })
            };

            await _collection.Indexes.CreateManyAsync(indexModels, cancellationToken);
            _logger.LogInformation("MongoDB conversion job indexes are ensured.");
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static bool TryNormalizeJobId(string jobId, out string normalizedJobId)
    {
        if (Guid.TryParse(jobId, out var parsedJobId))
        {
            normalizedJobId = parsedJobId.ToString("D");
            return true;
        }

        normalizedJobId = string.Empty;
        return false;
    }

    private static string NormalizeJobIdOrThrow(string jobId)
    {
        if (TryNormalizeJobId(jobId, out var normalizedJobId))
        {
            return normalizedJobId;
        }

        throw new InvalidOperationException("jobId must be a valid GUID.");
    }
}
