namespace DocumentConverter.Worker.Utilities;

public static class FileMoveHelper
{
    public static void MoveToDirectory(
        ILogger logger,
        string sourcePath,
        string targetDirectory,
        string targetName)
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

            logger.LogInformation(
                "Moved source file to {TargetName}. Source={SourceFileName}, Target={TargetPath}",
                targetName,
                fileName,
                targetPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to move source file. Source={SourcePath}", sourcePath);
        }
    }

    private static string GetUniqueTargetPath(string desiredPath)
    {
        if (!File.Exists(desiredPath))
        {
            return desiredPath;
        }

        var directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);
        var uniqueFileName =
            $"{fileNameWithoutExtension}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";

        return Path.Combine(directory, uniqueFileName);
    }
}
