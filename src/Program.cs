// ---------------------------------------------------------------------------
//  Triston's TidalRPC
//  Discord Rich Presence for the Tidal desktop app (via Windows SMTC).
//
//  Author : triston-dev  <https://github.com/triston-dev>
//  Product: Triston's TidalRPC
//  License: MIT
//
//  Program.cs — entry point. Enforces a single instance, sets up logging and
//  global exception handling, then runs the tray application context.
// ---------------------------------------------------------------------------

namespace TristonsTidalRPC;

internal static class Program
{
    // Per-user data lives here: config.json + log.txt.
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tristons TidalRPC");

    [STAThread]
    private static void Main()
    {
        // Single-instance guard. Holding the mutex for the process lifetime
        // stops a second copy from running.
        using var mutex = new Mutex(initiallyOwned: true,
            @"Local\TristonsTidalRPC_SingleInstance", out bool createdNew);
        if (!createdNew)
            return; // already running — quietly exit

        var config = Config.Load(Path.Combine(DataDir, "config.json"));
        Logger.Initialize(DataDir, config.Verbose);

        // Global exception handlers so an unexpected error is logged, not lost
        // to the void (there is no console).
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Logger.Error("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Error("Unhandled domain exception",
                e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));

        // WinForms init (equivalent to the generated ApplicationConfiguration.Initialize()).
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.Run(new TrayApp(config));
        }
        finally
        {
            GC.KeepAlive(mutex); // keep the mutex alive until the loop exits
        }
    }
}
