// ---------------------------------------------------------------------------
//  Triston's TidalRPC
//  Discord Rich Presence for the Tidal desktop app (via Windows SMTC).
//
//  Author : triston-dev  <https://github.com/triston-dev>
//  Product: Triston's TidalRPC
//  License: MIT
//
//  TrackInfo.cs — immutable snapshot of what Tidal is playing, built from the
//  SMTC media / playback / timeline properties.
// ---------------------------------------------------------------------------

using Windows.Media.Control;

namespace TristonsTidalRPC;

using PlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus;

/// <summary>A point-in-time snapshot of the current Tidal track and playback state.</summary>
internal sealed class TrackInfo
{
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string Album { get; init; } = "";

    public PlaybackStatus Status { get; init; }

    /// <summary>Playback position as of <see cref="LastUpdated"/>.</summary>
    public TimeSpan Position { get; init; }
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }

    /// <summary>When the timeline was last reported by SMTC (used to extrapolate elapsed time).</summary>
    public DateTimeOffset LastUpdated { get; init; }

    public bool IsPlaying => Status == PlaybackStatus.Playing;
    public bool IsPaused => Status == PlaybackStatus.Paused;

    public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;

    /// <summary>Stable identity used both for change-detection and as the album-art cache key.</summary>
    public string IdentityKey =>
        $"{Artist}|{Title}|{Album}".Trim().ToLowerInvariant();

    /// <summary>True if this looks like the same song as <paramref name="other"/> (ignores position/status).</summary>
    public bool SameTrack(TrackInfo? other) => other is not null && other.IdentityKey == IdentityKey;

    /// <summary>
    /// Heuristic ad detection. Tidal's free tier surfaces ads with an empty
    /// artist and/or a title like "Advertisement". This is intentionally
    /// conservative so we don't mistake a real track for an ad.
    /// </summary>
    public bool LooksLikeAd
    {
        get
        {
            string title = (Title ?? "").Trim();
            string artist = (Artist ?? "").Trim();

            if (title.Length == 0 && artist.Length == 0)
                return true;

            if (artist.Length == 0 &&
                (title.Equals("Advertisement", StringComparison.OrdinalIgnoreCase) ||
                 title.Contains("advertisement", StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }
    }

    public override string ToString() =>
        $"'{Title}' by '{Artist}' [{Status}] pos={Position:mm\\:ss}/{Duration:mm\\:ss}";
}
