// ---------------------------------------------------------------------------
//  Triston's TidalRPC
//  Discord Rich Presence for the Tidal desktop app (via Windows SMTC).
//
//  Author : triston-dev  <https://github.com/triston-dev>
//  Product: Triston's TidalRPC
//  License: MIT
//
//  TrayApp.cs — the tray UI (NotifyIcon + context menu) and the glue that
//  wires SmtcListener -> AlbumArtResolver -> DiscordPresenceManager together.
// ---------------------------------------------------------------------------

using System.Diagnostics;
using System.Reflection;

namespace TristonsTidalRPC;

/// <summary>
/// Application context for the background tray app. There is no main window;
/// the process lives in the notification area until the user quits.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string AppName = "Triston's TidalRPC";
    private const string GitHubUrl = "https://github.com/triston-dev";
    private const string Author = "triston-dev";

    private readonly Config _config;
    private readonly Control _marshal;      // hidden control -> installs the WinForms sync context
    private readonly NotifyIcon _tray;
    private readonly SmtcListener _smtc;
    private readonly DiscordPresenceManager _discord;
    private readonly AlbumArtResolver _art;

    private ToolStripMenuItem _miEnable = null!;
    private ToolStripMenuItem _miHideAds = null!;
    private ToolStripMenuItem _miStartup = null!;

    private TrackInfo? _lastTrack;

    public TrayApp(Config config)
    {
        _config = config;

        // Force-create a handle so a WindowsFormsSynchronizationContext is
        // installed on this (UI) thread *before* we capture it for the SMTC
        // listener. Accessing .Handle creates the (invisible) window.
        _marshal = new Control();
        _ = _marshal.Handle;

        _art = new AlbumArtResolver();
        _discord = new DiscordPresenceManager();
        _smtc = new SmtcListener(SynchronizationContext.Current!);
        _smtc.SessionsEnumerated += OnSessionsEnumerated;
        _smtc.TrackChanged += OnTrackChanged;

        // Reconcile the "run at startup" toggle with the actual registry state.
        _config.RunAtStartup = StartupManager.IsEnabled();

        _tray = new NotifyIcon
        {
            Icon = IconProvider.Load(),
            Text = AppName,
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => ShowCurrentTrackBalloon();

        if (_config.RpcEnabled)
            _discord.Start();

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _smtc.InitializeAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("SMTC initialization failed", ex);
            ShowBalloon("SMTC error", "Couldn't start media monitoring. See the log.", ToolTipIcon.Error);
        }
    }

    // ----- menu -------------------------------------------------------------

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var header = new ToolStripMenuItem(AppName) { Enabled = false };

        _miEnable = new ToolStripMenuItem("Rich Presence enabled", null, OnToggleEnabled)
        { Checked = _config.RpcEnabled, CheckOnClick = true };

        _miHideAds = new ToolStripMenuItem("Hide presence during ads", null, OnToggleHideAds)
        { Checked = _config.HideAds, CheckOnClick = true };

        _miStartup = new ToolStripMenuItem("Run at Windows startup", null, OnToggleStartup)
        { Checked = _config.RunAtStartup, CheckOnClick = true };

        var updates = new ToolStripMenuItem("Check for updates", null, OnCheckForUpdates);
        var about = new ToolStripMenuItem("About", null, OnAbout);
        var quit = new ToolStripMenuItem("Quit", null, (_, _) => ExitThread());

        menu.Items.AddRange(new ToolStripItem[]
        {
            header,
            new ToolStripSeparator(),
            _miEnable,
            _miHideAds,
            _miStartup,
            new ToolStripSeparator(),
            updates,
            about,
            new ToolStripSeparator(),
            quit,
        });
        return menu;
    }

    private void OnToggleEnabled(object? sender, EventArgs e)
    {
        _config.RpcEnabled = _miEnable.Checked;
        _config.Save();
        Logger.Info($"RPC enabled = {_config.RpcEnabled}");

        if (_config.RpcEnabled)
        {
            _discord.Start();
            _smtc.RequestRefresh();
        }
        else
        {
            _discord.Stop();
            UpdateTrayText(null);
        }
    }

    private void OnToggleHideAds(object? sender, EventArgs e)
    {
        _config.HideAds = _miHideAds.Checked;
        _config.Save();
        Logger.Info($"Hide-ads = {_config.HideAds}");
        // Re-evaluate the current track under the new setting.
        if (_lastTrack is not null) OnTrackChanged(_lastTrack);
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        _config.RunAtStartup = _miStartup.Checked;
        StartupManager.SetEnabled(_config.RunAtStartup);
        _config.Save();
    }

    private void OnCheckForUpdates(object? sender, EventArgs e)
    {
        // Kept intentionally simple: point the user at the GitHub page.
        OpenUrl($"{GitHubUrl}");
        ShowBalloon(AppName, $"You're running v{Application.ProductVersion}. Latest releases are on GitHub.", ToolTipIcon.Info);
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        string message =
            $"{AppName}\n" +
            $"Version {Application.ProductVersion}\n\n" +
            "Discord Rich Presence for the Tidal desktop app,\n" +
            "sourced from Windows System Media Transport Controls (SMTC).\n\n" +
            $"Author: {Author}\n" +
            $"{GitHubUrl}\n\n" +
            "Click Yes to open the GitHub page.";

        var result = MessageBox.Show(message, $"About — {AppName}",
            MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (result == DialogResult.Yes)
            OpenUrl(GitHubUrl);
    }

    // ----- SMTC callbacks (all on the UI thread) ----------------------------

    private void OnSessionsEnumerated(IReadOnlyList<string> aumids)
    {
        // Surface the discovered AUMIDs in the tray tooltip on first run so the
        // exact Tidal match string is easy to confirm. (Also written to the log.)
        Logger.Debug("Sessions enumerated: " + string.Join(", ", aumids));
    }

    private async void OnTrackChanged(TrackInfo? track)
    {
        _lastTrack = track;

        if (!_config.RpcEnabled)
        {
            UpdateTrayText(null);
            return;
        }

        if (track is null)
        {
            _discord.Clear();
            UpdateTrayText(null);
            return;
        }

        if (_config.HideAds && track.LooksLikeAd)
        {
            Logger.Debug("Ad detected — hiding presence.");
            _discord.Clear();
            UpdateTrayText(null);
            return;
        }

        UpdateTrayText(track);

        string? artUrl = null;
        try
        {
            artUrl = await _art.ResolveAsync(track);
        }
        catch (Exception ex)
        {
            Logger.Error("Album-art resolution failed", ex);
        }

        // The track may have changed while we were awaiting the art lookup;
        // only publish if this is still the current track.
        if (!ReferenceEquals(_lastTrack, track)) return;

        _discord.Update(track, artUrl);
    }

    // ----- tray helpers -----------------------------------------------------

    private void UpdateTrayText(TrackInfo? track)
    {
        string text = track is null
            ? $"{AppName} — idle"
            : $"{Truncate(track.Title, 30)} — {Truncate(track.Artist, 28)}";

        // NotifyIcon.Text is capped at 63 characters.
        _tray.Text = text.Length > 63 ? text[..62] + "…" : text;
    }

    private void ShowCurrentTrackBalloon()
    {
        var t = _lastTrack;
        if (t is null || !_config.RpcEnabled)
            ShowBalloon(AppName, "Nothing playing in Tidal.", ToolTipIcon.Info);
        else
            ShowBalloon(t.Title, string.IsNullOrWhiteSpace(t.Artist) ? "" : $"by {t.Artist}", ToolTipIcon.Info);
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        try
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = text;
            _tray.BalloonTipIcon = icon;
            _tray.ShowBalloonTip(3000);
        }
        catch { /* balloons are best-effort */ }
    }

    private static string Truncate(string s, int max)
    {
        s ??= "";
        return s.Length > max ? s[..(max - 1)] + "…" : s;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open URL {url}", ex);
        }
    }

    // ----- shutdown ---------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Logger.Info("Shutting down — cleaning up subscriptions and presence.");
            try { _tray.Visible = false; _tray.Dispose(); } catch { }
            try { _smtc.Dispose(); } catch { }
            try { _discord.Dispose(); } catch { }   // clears presence
            try { _art.Dispose(); } catch { }
            try { _marshal.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}

/// <summary>Loads the embedded application icon (falls back to a system icon).</summary>
internal static class IconProvider
{
    public static Icon Load()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            string? name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            if (name is not null)
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is not null) return new Icon(stream);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load embedded icon", ex);
        }
        return SystemIcons.Application;
    }
}
