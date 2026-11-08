using Microsoft.Win32;

namespace TinyHand;

/// <summary>"Open at login" through the per-user Run key.</summary>
internal static class LaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TinyHand";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value)
                {
                    key.SetValue(ValueName, Command);
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception e)
            {
                Log.Write($"launch at login change failed: {e.Message}");
            }
            Preferences.NotifyChanged();
        }
    }

    /// <summary>Keep the Run entry pointing at this exe if the app moved (e.g. after an update).</summary>
    public static void RefreshPath()
    {
        if (Enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) as string != Command)
            {
                key?.SetValue(ValueName, Command);
            }
        }
    }
}
