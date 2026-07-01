// ---------------------------------------------------------------------------
//  Triston's TidalRPC
//  Discord Rich Presence for the Tidal desktop app (via Windows SMTC).
//
//  Author : triston-dev  <https://github.com/triston-dev>
//  Product: Triston's TidalRPC
//  License: MIT
//
//  DiscordPresenceManager.cs — wraps the discord-rpc-csharp client. Builds a
//  RichPresence from a TrackInfo, derives accurate start/end timestamps from
//  the SMTC timeline, reflects paused state distinctly, and tolerates Discord
//  not being open yet (the client keeps trying to connect).
// ---------------------------------------------------------------------------

using DiscordRPC;
using DiscordRPC.Logging;

namespace TristonsTidalRPC;

/// <summary>Owns the Discord IPC client and translates tracks into Rich Presence.</summary>
internal sealed class DiscordPresenceManager : IDisposable
{
    // =========================================================================
    //  Discord Application ID — change this to point at your own Discord app.
    //  (Create one at https://discord.com/developers/applications)
    // =========================================================================
    public const string DiscordApplicationId = "1521933416495321321";

    // Rich Presence asset keys uploaded in the Discord Developer Portal
    // (App -> Rich Presence -> Art Assets). Used when no hosted cover art is
    // available and for the small play/pause status badge.
    public const string DefaultLargeImageKey = "tidal_logo";
    public const string PlayingSmallImageKey = "play";
    public const string PausedSmallImageKey = "pause";

    private DiscordRpcClient? _client;
    private string _lastSignature = "";

    /// <summary>Create and start the IPC client. Idempotent; safe to call after Stop().</summary>
    public void Start()
    {
        if (_client is not null && !_client.IsDisposed)
            return;

        _client = new DiscordRpcClient(DiscordApplicationId)
        {
            Logger = new DiscordLogAdapter { Level = LogLevel.Warning },
            // Don't auto-manage GameSense/idle; we drive presence explicitly.
            SkipIdenticalPresence = true,
        };

        _client.OnReady += (_, e) => Logger.Info($"Discord connected as {e.User.Username}");
        _client.OnConnectionFailed += (_, e) => Logger.Debug($"Discord connection attempt failed (pipe {e.FailedPipe}); will retry.");
        _client.OnConnectionEstablished += (_, e) => Logger.Debug($"Discord pipe {e.ConnectedPipe} established.");
        _client.OnError += (_, e) => Logger.Error($"Discord RPC error: {e.Code} {e.Message}");

        // Initialize() spins up a background thread that keeps trying to reach
        // Discord, so launching this before Discord is open is fine.
        _client.Initialize();
        _lastSignature = "";
        Logger.Info("Discord RPC client started.");
    }

    /// <summary>Clear presence and tear down the client (used when RPC is toggled off).</summary>
    public void Stop()
    {
        if (_client is null) return;
        try
        {
            if (!_client.IsDisposed)
            {
                _client.ClearPresence();
                _client.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Error stopping Discord client", ex);
        }
        _client = null;
        _lastSignature = "";
        Logger.Info("Discord RPC client stopped.");
    }

    /// <summary>Push a track to Discord. Deduplicates identical payloads.</summary>
    public void Update(TrackInfo track, string? artUrl)
    {
        if (_client is null || _client.IsDisposed) return;

        string large = string.IsNullOrEmpty(artUrl) ? DefaultLargeImageKey : artUrl;
        // Modern Discord clients accept a full https image URL as the image key
        // (they proxy it); the asset key is the fallback for the bundled logo.

        TimeSpan elapsed = CurrentElapsed(track);
        string total = track.Duration > TimeSpan.Zero ? FormatDuration(track.Duration) : "";
        string artist = string.IsNullOrWhiteSpace(track.Artist) ? "" : $"by {track.Artist}";

        // The State line always carries the total track length so "time total"
        // is visible. While playing, the live count-up timer below is the
        // "time played"; while paused (no timer) we show played / total
        // statically so both numbers are still there.
        string state = track.IsPlaying
            ? JoinDot(artist, total)
            : JoinDot(artist, total.Length > 0 ? $"{FormatDuration(elapsed)} / {total}" : "");

        var presence = new RichPresence
        {
            Details = ClampField(track.Title, "Unknown title"),
            State = ClampField(state, "Unknown artist"),
            Assets = new Assets
            {
                LargeImageKey = large,
                LargeImageText = string.IsNullOrWhiteSpace(track.Album) ? "Tidal" : Clamp(track.Album, 128),
                SmallImageKey = track.IsPlaying ? PlayingSmallImageKey : PausedSmallImageKey,
                SmallImageText = track.IsPlaying ? "Playing" : "Paused",
            },
        };

        // Use a START-ONLY timestamp while playing: Discord then renders a
        // count-UP "elapsed" timer (time played). Supplying End as well would
        // make Discord count *down* the remaining time, which is not what we
        // want. When paused we omit timestamps so there is no running timer.
        if (track.IsPlaying && track.Duration > TimeSpan.Zero)
        {
            DateTime startUtc = DateTime.UtcNow - elapsed;
            presence.Timestamps = new Timestamps(startUtc);
        }

        // Dedupe: skip if nothing meaningful changed (start rounded to the
        // second so a normal ticking clock doesn't spam SetPresence).
        string signature = BuildSignature(presence, track);
        if (signature == _lastSignature) return;
        _lastSignature = signature;

        try
        {
            _client.SetPresence(presence);
            Logger.Debug($"Presence set: {presence.Details} / {presence.State} (playing={track.IsPlaying}, elapsed={FormatDuration(elapsed)}/{(total.Length > 0 ? total : "?")})");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to set Discord presence", ex);
        }
    }

    /// <summary>Clear presence (Tidal closed / stopped / ad hidden) but keep the client alive.</summary>
    public void Clear()
    {
        if (_client is null || _client.IsDisposed) return;
        if (_lastSignature == "cleared") return;
        _lastSignature = "cleared";
        try
        {
            _client.ClearPresence();
            Logger.Debug("Presence cleared.");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to clear Discord presence", ex);
        }
    }

    private static string BuildSignature(RichPresence p, TrackInfo t)
    {
        long startSec = p.Timestamps?.Start is { } s ? new DateTimeOffset(s).ToUnixTimeSeconds() : 0;
        long endSec = p.Timestamps?.End is { } e ? new DateTimeOffset(e).ToUnixTimeSeconds() : 0;
        return string.Join('|', p.Details, p.State, p.Assets?.LargeImageKey,
            p.Assets?.SmallImageKey, t.Status, startSec, endSec);
    }

    // Discord requires Details/State to be 2..128 bytes; keep them in range.
    private static string ClampField(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) value = fallback;
        if (value.Length < 2) value = value.PadRight(2);
        return Clamp(value, 128);
    }

    private static string Clamp(string value, int max) =>
        value.Length > max ? value[..max] : value;

    /// <summary>
    /// Current playback position. SMTC reports Position as-of LastUpdated, so
    /// while playing we extrapolate to "now"; while paused the position is
    /// frozen (no extrapolation). Clamped to [0, Duration].
    /// </summary>
    private static TimeSpan CurrentElapsed(TrackInfo t)
    {
        TimeSpan e = t.Position;
        if (t.IsPlaying) e += DateTimeOffset.Now - t.LastUpdated;
        if (e < TimeSpan.Zero) e = TimeSpan.Zero;
        if (t.Duration > TimeSpan.Zero && e > t.Duration) e = t.Duration;
        return e;
    }

    private static string FormatDuration(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    /// <summary>Joins non-empty parts with a bullet separator, e.g. "by X • 4:12".</summary>
    private static string JoinDot(params string[] parts) =>
        string.Join("  •  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    public void Dispose()
    {
        try { _client?.ClearPresence(); } catch { /* best effort on shutdown */ }
        try { _client?.Dispose(); } catch { /* ignore */ }
        _client = null;
    }

    /// <summary>Routes the discord-rpc-csharp library's own logging into our file log.</summary>
    private sealed class DiscordLogAdapter : ILogger
    {
        public LogLevel Level { get; set; } = LogLevel.Warning;

        public void Trace(string message, params object[] args)
        {
            if (Level <= LogLevel.Trace) Logger.Debug("[discord] " + SafeFormat(message, args));
        }

        public void Info(string message, params object[] args)
        {
            if (Level <= LogLevel.Info) Logger.Info("[discord] " + SafeFormat(message, args));
        }

        public void Warning(string message, params object[] args)
        {
            if (Level <= LogLevel.Warning) Logger.Info("[discord][warn] " + SafeFormat(message, args));
        }

        public void Error(string message, params object[] args)
        {
            if (Level <= LogLevel.Error) Logger.Error("[discord] " + SafeFormat(message, args));
        }

        private static string SafeFormat(string message, object[] args)
        {
            try { return args is { Length: > 0 } ? string.Format(message, args) : message; }
            catch { return message; }
        }
    }
}
