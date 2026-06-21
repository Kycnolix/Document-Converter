using Microsoft.AspNetCore.Mvc;

namespace DocumentConverter.Api.Models;

public sealed class ConversionUploadRequest
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    [FromForm(Name = "targetFormat")]
    public string? TargetFormat { get; init; }
}
