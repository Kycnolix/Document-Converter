namespace DocumentConverter.Shared.Options;

public sealed class MongoOptions
{
    public string ConnectionString { get; set; } = "mongodb://host.docker.internal:27017";
    public string DatabaseName { get; set; } = "document_converter";
    public string ConversionJobsCollectionName { get; set; } = "conversionJobs";
}
