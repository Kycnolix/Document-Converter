using DocumentConverter.Api.Models;
using DocumentConverter.Api.Options;
using DocumentConverter.Api.Services;
using DocumentConverter.Shared.Models;
using DocumentConverter.Shared.Options;
using DocumentConverter.Shared.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var converterOptions = builder.Configuration.GetSection("Converter").Get<ConverterStorageOptions>() ?? new ConverterStorageOptions();
var mongoOptions = builder.Configuration.GetSection("Mongo").Get<MongoOptions>() ?? new MongoOptions();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = converterOptions.MaxUploadBytes;
});

builder.Services.Configure<ConverterStorageOptions>(builder.Configuration.GetSection("Converter"));
builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection("Mongo"));
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = converterOptions.MaxUploadBytes;
});
builder.Services.AddSingleton(sp =>
    new MongoConversionJobStore(
        sp.GetRequiredService<ILogger<MongoConversionJobStore>>(),
        sp.GetRequiredService<IOptions<MongoOptions>>().Value));
builder.Services.AddSingleton<IConversionJobStore>(sp => sp.GetRequiredService<MongoConversionJobStore>());
builder.Services.AddSingleton<ConversionJobService>();
builder.Services.AddSingleton<ReadinessService>();

var app = builder.Build();

app.Services.GetRequiredService<ConversionJobService>().EnsureDirectories();
app.Logger.LogInformation("document-converter API started at {Time}", DateTimeOffset.Now);
app.Logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);
app.Logger.LogInformation("InputRoot: {InputRoot}", converterOptions.InputRoot);
app.Logger.LogInformation("OutputRoot: {OutputRoot}", converterOptions.OutputRoot);
app.Logger.LogInformation("ProcessedRoot: {ProcessedRoot}", converterOptions.ProcessedRoot);
app.Logger.LogInformation("FailedRoot: {FailedRoot}", converterOptions.FailedRoot);
app.Logger.LogInformation("TempRoot: {TempRoot}", converterOptions.TempRoot);
app.Logger.LogInformation("JobsRoot: {JobsRoot}", converterOptions.JobsRoot);
app.Logger.LogInformation("MaxUploadBytes: {MaxUploadBytes}", converterOptions.MaxUploadBytes);
app.Logger.LogInformation("MongoDatabaseName: {MongoDatabaseName}", mongoOptions.DatabaseName);
app.Logger.LogInformation("MongoCollectionName: {MongoCollectionName}", mongoOptions.ConversionJobsCollectionName);

app.MapGet("/health", () =>
{
    return Results.Ok(ApiResponse.Success(new HealthResponse
    {
        Status = "Healthy"
    }));
});

app.MapGet("/ready", async (
    ReadinessService readinessService,
    CancellationToken cancellationToken) =>
{
    var (isReady, readinessResponse) = await readinessService.CheckAsync(cancellationToken);

    if (isReady)
    {
        return Results.Ok(ApiResponse.Success(readinessResponse));
    }

    return Results.Json(
        ApiResponse.Error("Service is not ready.", readinessResponse),
        statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapPost("/api/conversions", async (
    [FromForm] IFormFile? file,
    [FromForm] string? targetFormat,
    ConversionJobService conversionJobService,
    CancellationToken cancellationToken) =>
{
    var normalizedTargetFormat = string.IsNullOrWhiteSpace(targetFormat) ? "pdf" : targetFormat.Trim().ToLowerInvariant();

    if (file is null)
    {
        return Results.BadRequest(ApiResponse.Error(
            "A file upload is required.",
            ValidationErrorData.FromError("file", "The file field is required.")));
    }

    if (file.Length == 0)
    {
        return Results.BadRequest(ApiResponse.Error(
            "The uploaded file is empty.",
            ValidationErrorData.FromError("file", "The uploaded file must not be empty.")));
    }

    if (file.Length > conversionJobService.MaxUploadBytes)
    {
        return Results.BadRequest(ApiResponse.Error(
            "The uploaded file exceeds the maximum allowed size.",
            ValidationErrorData.FromError(
                "file",
                $"The uploaded file must be {conversionJobService.MaxUploadBytes} bytes or smaller.")));
    }

    if (!string.Equals(normalizedTargetFormat, "pdf", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(ApiResponse.Error(
            "Only PDF target format is supported.",
            ValidationErrorData.FromError("targetFormat", "Only pdf is supported right now.")));
    }

    var sourceExtension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();

    if (!conversionJobService.IsSupportedSourceExtension(sourceExtension))
    {
        return Results.BadRequest(ApiResponse.Error(
            "The uploaded file type is not supported.",
            ValidationErrorData.FromError("file", "Unsupported file extension.")));
    }

    try
    {
        var metadata = await conversionJobService.CreateJobAsync(file, normalizedTargetFormat, cancellationToken);

        return Results.Ok(ApiResponse.Success(new ConversionCreateResponse
        {
            JobId = metadata.JobId,
            Status = ConversionJobStatus.Pending,
            ResultUrl = null
        }));
    }
    catch
    {
        return Results.Json(
            ApiResponse.Error("Conversion job storage is currently unavailable."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.DisableAntiforgery();

app.MapGet("/api/conversions/{jobId}", async (
    string jobId,
    ConversionJobService conversionJobService,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(jobId, out var parsedJobId))
    {
        return Results.BadRequest(ApiResponse.Error("The supplied jobId is not a valid GUID."));
    }

    try
    {
        var metadata = await conversionJobService.GetMetadataAsync(parsedJobId.ToString("D"), cancellationToken);

        if (metadata is null)
        {
            return Results.NotFound(ApiResponse.Error("Conversion job was not found."));
        }

        var status = conversionJobService.GetStatus(metadata);
        return Results.Ok(ApiResponse.Success(status));
    }
    catch
    {
        return Results.Json(
            ApiResponse.Error("Conversion job storage is currently unavailable."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/conversions/{jobId}/result", async (
    string jobId,
    ConversionJobService conversionJobService,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(jobId, out var parsedJobId))
    {
        return Results.BadRequest(ApiResponse.Error("The supplied jobId is not a valid GUID."));
    }

    try
    {
        var metadata = await conversionJobService.GetMetadataAsync(parsedJobId.ToString("D"), cancellationToken);

        if (metadata is null)
        {
            return Results.NotFound(ApiResponse.Error("Conversion job was not found."));
        }

        var outputFilePath = conversionJobService.GetOutputFilePath(metadata);

        if (!File.Exists(outputFilePath))
        {
            return Results.Conflict(ApiResponse.Error("Conversion result is not available yet."));
        }

        return Results.File(
            outputFilePath,
            "application/pdf",
            conversionJobService.GetResultDownloadFileName(metadata));
    }
    catch
    {
        return Results.Json(
            ApiResponse.Error("Conversion job storage is currently unavailable."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();
