// ---------------------------------------------------------------------------
//  Triston's TidalRPC
//  Discord Rich Presence for the Tidal desktop app (via Windows SMTC).
//
//  Author : triston-dev  <https://github.com/triston-dev>
//  Product: Triston's TidalRPC
//  License: MIT
//
//  Logger.cs — tiny thread-safe file logger. There is no console window
//  (WinExe), so all diagnostics go to %APPDATA%\Tristons TidalRPC\log.txt.
// ---------------------------------------------------------------------------

namespace TristonsTidalRPC;

/// <summary>Minimal append-only file logger with a verbosity gate.</summary>
internal static class Logger
{
    private static readonly object Gate = new();
    private static string _logPath = "";
    private static bool _verbose;

    /// <summary>Point the logger at a directory and set verbosity. Safe to call once at startup.</summary>
    public static void Initialize(string directory, bool verbose)
    {
        _verbose = verbose;
        try
        {
            Directory.CreateDirectory(directory);
            _logPath = Path.Combine(directory, "log.txt");

            // Keep the log from growing without bound across runs.
            if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 1_000_000)
                File.Delete(_logPath);
        }
        catch
        {
            // Logging must never take the app down.
            _logPath = "";
        }

        Info($"=== Triston's TidalRPC v{Application.ProductVersion} started {DateTimeOffset.Now:O} (verbose={verbose}) ===");
    }

    public static void SetVerbose(bool verbose) => _verbose = verbose;

    public static string LogPath => _logPath;

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex) => Write("ERROR", $"{message} :: {ex}");

    /// <summary>Written only when verbose logging is enabled.</summary>
    public static void Debug(string message)
    {
        if (_verbose) Write("DEBUG", message);
    }

    private static void Write(string level, string message)
    {
        string line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        if (_logPath.Length == 0) return;
        lock (Gate)
        {
            try { File.AppendAllText(_logPath, line + Environment.NewLine); }
            catch { /* swallow — never crash on a log write */ }
        }
    }
}
