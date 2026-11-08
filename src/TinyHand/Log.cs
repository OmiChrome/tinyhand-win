namespace TinyHand;

internal static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static string FilePath => Path.Combine(Paths.LocalData, "TinyHand.log");

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }
                File.AppendAllText(FilePath, line);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
#if DEBUG
        System.Diagnostics.Debug.Write(line);
#endif
    }
}
