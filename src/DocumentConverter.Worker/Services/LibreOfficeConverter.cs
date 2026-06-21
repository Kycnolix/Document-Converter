using System.Diagnostics;
using DocumentConverter.Worker.Models;
using DocumentConverter.Worker.Options;
using Microsoft.Extensions.Options;

namespace DocumentConverter.Worker.Services;

public sealed class LibreOfficeConverter
{
    private readonly ILogger<LibreOfficeConverter> _logger;
    private readonly ConverterOptions _options;
    private const string LibreOfficeExitNonZeroErrorCode = "LIBREOFFICE_EXIT_NON_ZERO";
    private const string OutputNotFoundErrorCode = "OUTPUT_NOT_FOUND";
    private const string ConversionTimeoutErrorCode = "CONVERSION_TIMEOUT";
    private const string ConversionExceptionErrorCode = "CONVERSION_EXCEPTION";

    public LibreOfficeConverter(ILogger<LibreOfficeConverter> logger, IOptions<ConverterOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<ConversionResult> ConvertToPdfAsync(
        string sourcePath,
        string targetPath,
        CancellationToken stoppingToken)
    {
        var sourceFileName = Path.GetFileName(sourcePath);
        var profilePath = Path.Combine(_options.TempRoot, "lo-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profilePath);

        try
        {
            _logger.LogInformation("Converting {SourceFileName} to PDF.", sourceFileName);

            var profileUri = ToLibreOfficeFileUri(profilePath);
            var startInfo = CreateBaseStartInfo();

            startInfo.ArgumentList.Add($"-env:UserInstallation={profileUri}");
            startInfo.ArgumentList.Add("--headless");
            startInfo.ArgumentList.Add("--nologo");
            startInfo.ArgumentList.Add("--nofirststartwizard");
            startInfo.ArgumentList.Add("--convert-to");
            startInfo.ArgumentList.Add("pdf");
            startInfo.ArgumentList.Add("--outdir");
            startInfo.ArgumentList.Add(_options.OutputRoot);
            startInfo.ArgumentList.Add(sourcePath);

            using var process = new Process
            {
                StartInfo = startInfo
            };

            process.Start();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ConversionTimeoutSeconds));

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
                        stderr);

                    return new ConversionResult
                    {
                        Success = false,
                        ErrorCode = LibreOfficeExitNonZeroErrorCode,
                        ErrorMessage = GetNonZeroExitMessage(process.ExitCode, stderr)
                    };
                }

                if (!File.Exists(targetPath))
                {
                    _logger.LogError(
                        "Conversion finished but output file was not found. Source={SourcePath}, ExpectedOutput={TargetPath}",
                        sourcePath,
                        targetPath);

                    return new ConversionResult
                    {
                        Success = false,
                        ErrorCode = OutputNotFoundErrorCode,
                        ErrorMessage = "Output file was not found after conversion."
                    };
                }

                return new ConversionResult
                {
                    Success = true
                };
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                TryKillProcess(process);

                _logger.LogError(
                    "Conversion timed out after {TimeoutSeconds} seconds. Source={SourceFileName}",
                    _options.ConversionTimeoutSeconds,
                    sourceFileName);

                return new ConversionResult
                {
                    Success = false,
                    ErrorCode = ConversionTimeoutErrorCode,
                    ErrorMessage = $"Conversion timed out after {_options.ConversionTimeoutSeconds} seconds."
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversion threw an exception. Source={SourceFileName}", sourceFileName);

            return new ConversionResult
            {
                Success = false,
                ErrorCode = ConversionExceptionErrorCode,
                ErrorMessage = "Conversion failed because an unexpected error occurred."
            };
        }
        finally
        {
            TryDeleteDirectory(profilePath);
        }
    }

    public async Task LogVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = CreateBaseStartInfo();
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
                stderr);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LibreOffice check threw an exception.");
        }
    }

    private ProcessStartInfo CreateBaseStartInfo()
    {
        return new ProcessStartInfo
        {
            FileName = _options.LibreOfficePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private string ToLibreOfficeFileUri(string path)
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

    private static string GetNonZeroExitMessage(int exitCode, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return $"LibreOffice exited with code {exitCode}.";
        }

        return $"LibreOffice exited with code {exitCode}: {stderr.Trim()}";
    }
}
