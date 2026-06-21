using DocumentConverter.Worker.Options;
using DocumentConverter.Worker.Utilities;
using Microsoft.Extensions.Options;

namespace DocumentConverter.Worker.Services;

public sealed class InputFolderProcessor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
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

    private readonly ILogger<InputFolderProcessor> _logger;
    private readonly ConverterOptions _options;
    private readonly LibreOfficeConverter _libreOfficeConverter;

    public InputFolderProcessor(
        ILogger<InputFolderProcessor> logger,
        IOptions<ConverterOptions> options,
        LibreOfficeConverter libreOfficeConverter)
    {
        _logger = logger;
        _options = options.Value;
        _libreOfficeConverter = libreOfficeConverter;
    }

    public async Task ProcessAsync(CancellationToken stoppingToken)
    {
        var files = Directory
            .EnumerateFiles(_options.InputRoot)
            .Where(IsSupportedFile)
            .OrderBy(File.GetCreationTimeUtc)
            .ToList();

        if (files.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {FileCount} input file(s).", files.Count);

        foreach (var sourcePath in files)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var targetPath = GetTargetPath(sourcePath);

            if (File.Exists(targetPath))
            {
                _logger.LogInformation(
                    "Output already exists. Moving source to processed. Source={SourceFileName}, Output={OutputFileName}",
                    Path.GetFileName(sourcePath),
                    Path.GetFileName(targetPath));

                MoveToProcessed(sourcePath);
                continue;
            }

            var result = await _libreOfficeConverter.ConvertToPdfAsync(sourcePath, targetPath, stoppingToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Conversion succeeded. Source={SourceFileName}, Output={TargetFileName}",
                    Path.GetFileName(sourcePath),
                    Path.GetFileName(targetPath));

                MoveToProcessed(sourcePath);
                continue;
            }

            MoveToFailed(sourcePath);
        }
    }

    private bool IsSupportedFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension);
    }

    private string GetTargetPath(string sourcePath)
    {
        var targetFileName = Path.GetFileNameWithoutExtension(sourcePath) + ".pdf";
        return Path.Combine(_options.OutputRoot, targetFileName);
    }

    private void MoveToProcessed(string sourcePath)
    {
        FileMoveHelper.MoveToDirectory(_logger, sourcePath, _options.ProcessedRoot, "processed");
    }

    private void MoveToFailed(string sourcePath)
    {
        FileMoveHelper.MoveToDirectory(_logger, sourcePath, _options.FailedRoot, "failed");
    }
}
