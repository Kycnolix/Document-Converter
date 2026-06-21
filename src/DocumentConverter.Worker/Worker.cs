using System.Diagnostics;

namespace DocumentConverter.Worker;

public sealed class Worker : BackgroundService
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

    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;

    private string _inputRoot = "/app/data/input";
    private string _outputRoot = "/app/data/output";
    private string _processedRoot = "/app/data/processed";
    private string _failedRoot = "/app/data/failed";
    private string _tempRoot = "/app/data/temp";
    private string _libreOfficePath = "soffice";

    private int _conversionTimeoutSeconds = 120;
    private int _pollingIntervalSeconds = 30;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LoadOptions();
        EnsureDirectories();

        _logger.LogInformation("document-converter worker started at {Time}", DateTimeOffset.Now);
        _logger.LogInformation("InputRoot: {InputRoot}", _inputRoot);
        _logger.LogInformation("OutputRoot: {OutputRoot}", _outputRoot);
        _logger.LogInformation("ProcessedRoot: {ProcessedRoot}", _processedRoot);
        _logger.LogInformation("FailedRoot: {FailedRoot}", _failedRoot);
        _logger.LogInformation("TempRoot: {TempRoot}", _tempRoot);
        _logger.LogInformation("PollingIntervalSeconds: {PollingIntervalSeconds}", _pollingIntervalSeconds);

        await LogLibreOfficeVersionAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessInputFilesAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(_pollingIntervalSeconds), stoppingToken);
        }
    }

    private void LoadOptions()
    {
        _inputRoot = _configuration["Converter:InputRoot"] ?? _inputRoot;
        _outputRoot = _configuration["Converter:OutputRoot"] ?? _outputRoot;
        _processedRoot = _configuration["Converter:ProcessedRoot"] ?? _processedRoot;
        _failedRoot = _configuration["Converter:FailedRoot"] ?? _failedRoot;
        _tempRoot = _configuration["Converter:TempRoot"] ?? _tempRoot;
        _libreOfficePath = _configuration["Converter:LibreOfficePath"] ?? _libreOfficePath;

        if (int.TryParse(_configuration["Converter:ConversionTimeoutSeconds"], out var timeoutSeconds))
        {
            _conversionTimeoutSeconds = timeoutSeconds;
        }

        if (int.TryParse(_configuration["Converter:PollingIntervalSeconds"], out var pollingSeconds))
        {
            _pollingIntervalSeconds = pollingSeconds;
        }
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_inputRoot);
        Directory.CreateDirectory(_outputRoot);
        Directory.CreateDirectory(_processedRoot);
        Directory.CreateDirectory(_failedRoot);
        Directory.CreateDirectory(_tempRoot);
    }

    private async Task ProcessInputFilesAsync(CancellationToken stoppingToken)
    {
        var files = Directory
            .EnumerateFiles(_inputRoot)
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
                    Path.GetFileName(targetPath)
                );

                MoveToProcessed(sourcePath);
                continue;
            }

            await ConvertToPdfAsync(sourcePath, targetPath, stoppingToken);
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

        return Path.Combine(_outputRoot, targetFileName);
    }

    private async Task ConvertToPdfAsync(string sourcePath, string targetPath, CancellationToken stoppingToken)
    {
        var sourceFileName = Path.GetFileName(sourcePath);
        var targetFileName = Path.GetFileName(targetPath);

        var profilePath = Path.Combine(_tempRoot, "lo-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profilePath);

        try
        {
            _logger.LogInformation("Converting {SourceFileName} to PDF.", sourceFileName);

            var profileUri = ToLibreOfficeFileUri(profilePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = _libreOfficePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add($"-env:UserInstallation={profileUri}");
            startInfo.ArgumentList.Add("--headless");
            startInfo.ArgumentList.Add("--nologo");
            startInfo.ArgumentList.Add("--nofirststartwizard");
            startInfo.ArgumentList.Add("--convert-to");
            startInfo.ArgumentList.Add("pdf");
            startInfo.ArgumentList.Add("--outdir");
            startInfo.ArgumentList.Add(_outputRoot);
            startInfo.ArgumentList.Add(sourcePath);

            using var process = new Process
            {
                StartInfo = startInfo
            };

            process.Start();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_conversionTimeoutSeconds));

            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

                await process.WaitForExitAsync(timeoutCts.Token);

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    _logger.LogError(
                        "Conversion failed for {SourceFileName}. ExitCode={ExitCode}. StdOut={StdOut}. StdErr={StdErr}",
                        sourceFileName,
                        process.ExitCode,
                        stdout,
                        stderr
                    );

                    MoveToFailed(sourcePath);
                    return;
                }

                if (!File.Exists(targetPath))
                {
                    _logger.LogError(
                        "Conversion finished but output file was not found. Source={SourcePath}, ExpectedOutput={TargetPath}",
                        sourcePath,
                        targetPath
                    );

                    MoveToFailed(sourcePath);
                    return;
                }

                _logger.LogInformation(
                    "Conversion succeeded. Source={SourceFileName}, Output={TargetFileName}",
                    sourceFileName,
                    targetFileName
                );

                MoveToProcessed(sourcePath);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                TryKillProcess(process);

                _logger.LogError(
                    "Conversion timed out after {TimeoutSeconds} seconds. Source={SourceFileName}",
                    _conversionTimeoutSeconds,
                    sourceFileName
                );

                MoveToFailed(sourcePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversion threw an exception. Source={SourceFileName}", sourceFileName);

            MoveToFailed(sourcePath);
        }
        finally
        {
            TryDeleteDirectory(profilePath);
        }
    }

    private async Task LogLibreOfficeVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _libreOfficePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("--version");

            using var process = new Process
            {
                StartInfo = startInfo
            };

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("LibreOffice detected: {Version}", stdout.Trim());
                return;
            }

            _logger.LogError(
                "LibreOffice check failed. ExitCode={ExitCode}, StdErr={StdErr}",
                process.ExitCode,
                stderr
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LibreOffice check threw an exception.");
        }
    }

    private void MoveToProcessed(string sourcePath)
    {
        MoveFileToDirectory(sourcePath, _processedRoot, "processed");
    }

    private void MoveToFailed(string sourcePath)
    {
        MoveFileToDirectory(sourcePath, _failedRoot, "failed");
    }

    private void MoveFileToDirectory(string sourcePath, string targetDirectory, string targetName)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return;
            }

            Directory.CreateDirectory(targetDirectory);

            var fileName = Path.GetFileName(sourcePath);
            var targetPath = GetUniqueTargetPath(Path.Combine(targetDirectory, fileName));

            File.Move(sourcePath, targetPath);

            _logger.LogInformation(
                "Moved source file to {TargetName}. Source={SourceFileName}, Target={TargetPath}",
                targetName,
                fileName,
                targetPath
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to move source file. Source={SourcePath}", sourcePath);
        }
    }

    private static string GetUniqueTargetPath(string desiredPath)
    {
        if (!File.Exists(desiredPath))
        {
            return desiredPath;
        }

        var directory = Path.GetDirectoryName(desiredPath) ?? "";
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);

        var uniqueFileName = $"{fileNameWithoutExtension}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";

        return Path.Combine(directory, uniqueFileName);
    }

    private static string ToLibreOfficeFileUri(string path)
    {
        var fullPath = Path.GetFullPath(path).Replace("\\", "/");

        if (fullPath.StartsWith('/'))
        {
            return "file://" + fullPath;
        }

        return "file:///" + fullPath;
    }

    private void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill LibreOffice process.");
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp LibreOffice profile directory: {Path}", path);
        }
    }
}