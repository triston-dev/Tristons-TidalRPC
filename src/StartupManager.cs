// ---------------------------------------------------------------------------
//  Triston's TidalRPC
//  Discord Rich Presence for the Tidal desktop app (via Windows SMTC).
//
//  Author : triston-dev  <https://github.com/triston-dev>
//  Product: Triston's TidalRPC
//  License: MIT
//
//  StartupManager.cs — "Run at Windows startup" via the per-user Run key
//  (HKCU\...\CurrentVersion\Run). No admin rights required.
// ---------------------------------------------------------------------------

using Microsoft.Win32;

namespace TristonsTidalRPC;

/// <summary>Reads/writes the HKCU Run entry that launches the app at logon.</summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Tristons TidalRPC";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string v && v.Length > 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to read startup state", ex);
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return;

            if (enabled)
            {
                // Environment.ProcessPath is the real exe path (works for a
                // single-file publish too, unlike Assembly.Location).
                string exe = Environment.ProcessPath ?? Application.ExecutablePath;
                key.SetValue(ValueName, $"\"{exe}\"");
                Logger.Info($"Enabled run-at-startup: {exe}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Logger.Info("Disabled run-at-startup.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to set startup state", ex);
        }
    }
}
