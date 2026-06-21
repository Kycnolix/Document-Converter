using DocumentConverter.Shared.Models;

namespace DocumentConverter.Shared.Storage;

public interface IConversionJobStore
{
    Task CreateAsync(ConversionJobMetadata metadata, CancellationToken cancellationToken);
    Task DeleteAsync(string jobId, CancellationToken cancellationToken);
    Task<ConversionJobMetadata?> GetByJobIdAsync(string jobId, CancellationToken cancellationToken);
    Task<ConversionJobMetadata?> GetByStoredSourceFileNameAsync(string storedSourceFileName, CancellationToken cancellationToken);
    Task<ConversionJobMetadata?> ClaimNextPendingAsync(string workerId, TimeSpan lockDuration, CancellationToken cancellationToken);
    Task MarkReadyAsync(string jobId, CancellationToken cancellationToken);
    Task MarkFailedAsync(string jobId, string errorCode, string errorMessage, CancellationToken cancellationToken);
    Task MarkUnsupportedAsync(string jobId, string errorCode, string errorMessage, CancellationToken cancellationToken);
}
