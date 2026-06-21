using DocumentConverter.Api.Models;
using DocumentConverter.Api.Options;
using DocumentConverter.Shared.Storage;
using Microsoft.Extensions.Options;

namespace DocumentConverter.Api.Services;

public sealed class ReadinessService
{
    private readonly ConverterStorageOptions _converterOptions;
    private readonly MongoConversionJobStore _mongoConversionJobStore;
    private readonly ILogger<ReadinessService> _logger;

    public ReadinessService(
        IOptions<ConverterStorageOptions> converterOptions,
        MongoConversionJobStore mongoConversionJobStore,
        ILogger<ReadinessService> logger)
    {
        _converterOptions = converterOptions.Value;
        _mongoConversionJobStore = mongoConversionJobStore;
        _logger = logger;
    }

    public async Task<(bool IsReady, ReadinessResponse Response)> CheckAsync(CancellationToken cancellationToken)
    {
        var checks = new List<ReadinessCheckResponse>();

        try
        {
            foreach (var directoryPath in _converterOptions.GetManagedDirectories())
            {
                Directory.CreateDirectory(directoryPath);
            }

            checks.Add(new ReadinessCheckResponse
            {
                Name = "DataDirectories",
                IsHealthy = true,
                Message = "Required data directories are available."
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Readiness check failed while validating data directories.");
            checks.Add(new ReadinessCheckResponse
            {
                Name = "DataDirectories",
                IsHealthy = false,
                Message = "Required data directories are not available."
            });
        }

        try
        {
            await _mongoConversionJobStore.CheckAvailabilityAsync(cancellationToken);
            checks.Add(new ReadinessCheckResponse
            {
                Name = "MongoDB",
                IsHealthy = true,
                Message = "MongoDB is reachable."
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Readiness check failed while validating MongoDB connectivity.");
            checks.Add(new ReadinessCheckResponse
            {
                Name = "MongoDB",
                IsHealthy = false,
                Message = "MongoDB is not reachable or initialization failed."
            });
        }

        var isReady = checks.All(x => x.IsHealthy);

        return (
            isReady,
            new ReadinessResponse
            {
                Status = isReady ? "Ready" : "NotReady",
                Checks = isReady ? null : checks
            });
    }
}
