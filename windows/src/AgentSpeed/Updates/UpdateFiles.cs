namespace AgentSpeed.Updates;

internal static class UpdateFiles
{
    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.AppLog.Warn($"Could not remove an update temporary folder: {UpdateLog.Describe(ex)}");
        }
    }

    public static bool TryDelete(string path, int attempts = 1, int delayMs = 0)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= attempts)
                {
                    return false;
                }

                Thread.Sleep(delayMs);
            }
        }
    }
}
