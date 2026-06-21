using DocumentConverter.Api.Models;
using DocumentConverter.Api.Options;
using DocumentConverter.Api.Services;
using DocumentConverter.Shared.Models;
using DocumentConverter.Shared.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ConverterStorageOptions>(builder.Configuration.GetSection("Converter"));
builder.Services.AddSingleton(sp =>
    new ConversionJobMetadataStore(
        sp.GetRequiredService<ILogger<ConversionJobMetadataStore>>(),
        sp.GetRequiredService<IOptions<ConverterStorageOptions>>().Value.JobsRoot));
builder.Services.AddSingleton<ConversionJobService>();

var app = builder.Build();

app.Services.GetRequiredService<ConversionJobService>().EnsureDirectories();

app.MapGet("/health", () =>
{
    return Results.Ok(ApiResponse.Success(new HealthResponse
    {
        Status = "Healthy"
    }));
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

    var metadata = await conversionJobService.CreateJobAsync(file, normalizedTargetFormat, cancellationToken);

    return Results.Ok(ApiResponse.Success(new ConversionCreateResponse
    {
        JobId = metadata.JobId,
        Status = ConversionJobStatus.Pending,
        ResultUrl = null
    }));
})
.DisableAntiforgery();

app.MapGet("/api/conversions/{jobId}", async (
    string jobId,
    ConversionJobService conversionJobService,
    CancellationToken cancellationToken) =>
{
    if (!ConversionJobMetadataStore.TryNormalizeJobId(jobId, out var normalizedJobId))
    {
        return Results.BadRequest(ApiResponse.Error("The supplied jobId is not a valid GUID."));
    }

    var metadata = await conversionJobService.GetMetadataAsync(normalizedJobId, cancellationToken);

    if (metadata is null)
    {
        return Results.NotFound(ApiResponse.Error("Conversion job was not found."));
    }

    var status = conversionJobService.GetStatus(metadata);
    return Results.Ok(ApiResponse.Success(status));
});

app.MapGet("/api/conversions/{jobId}/result", async (
    string jobId,
    ConversionJobService conversionJobService,
    CancellationToken cancellationToken) =>
{
    if (!ConversionJobMetadataStore.TryNormalizeJobId(jobId, out var normalizedJobId))
    {
        return Results.BadRequest(ApiResponse.Error("The supplied jobId is not a valid GUID."));
    }

    var metadata = await conversionJobService.GetMetadataAsync(normalizedJobId, cancellationToken);

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
});

app.Run();
