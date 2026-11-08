namespace TinyHand;

internal static class Paths
{
    /// <summary>%APPDATA%\TinyHand — settings and custom hands (roams with the user).</summary>
    public static string RoamingData { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TinyHand"));

    /// <summary>%LOCALAPPDATA%\TinyHand.Data — logs and machine-local state.</summary>
    public static string LocalData { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyHand.Data"));

    public static string AppDir { get; } = AppContext.BaseDirectory;

    /// <summary>Find a bundled asset next to the exe, falling back to the repo's assets folder in dev.</summary>
    public static string? Asset(string relative)
    {
        string[] candidates =
        {
            Path.Combine(AppDir, "Assets", relative),
            Path.Combine(AppDir, relative),
            Path.Combine(Environment.CurrentDirectory, "assets", relative),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
