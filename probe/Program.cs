// ---------------------------------------------------------------------------
//  Triston's TidalRPC — SMTC enumeration probe
//  Author: triston-dev  <https://github.com/triston-dev>
//
//  Throwaway diagnostic: enumerates every System Media Transport Controls
//  (SMTC) session, prints each SourceAppUserModelId (AUMID) and its current
//  media / playback / timeline properties. Purpose is to confirm the exact
//  AUMID string that the Tidal desktop app reports so we can match it
//  explicitly in the real application (instead of grabbing the default
//  session and letting other media apps hijack the presence).
// ---------------------------------------------------------------------------

using Windows.Media.Control;

using GsmtcSessionManager =
    Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("Triston's TidalRPC — SMTC probe");
Console.WriteLine("================================\n");

// RequestAsync() asks Windows for the session manager. Awaiting a WinRT
// IAsyncOperation<T> works directly thanks to the built-in CsWinRT projections.
GsmtcSessionManager manager = await GsmtcSessionManager.RequestAsync();

var sessions = manager.GetSessions();
var current = manager.GetCurrentSession();

Console.WriteLine($"Sessions found: {sessions.Count}");
Console.WriteLine($"Current/default session AUMID: " +
    $"{(current is null ? "<none>" : current.SourceAppUserModelId)}\n");

int index = 0;
foreach (var session in sessions)
{
    index++;
    string aumid = session.SourceAppUserModelId;
    bool looksLikeTidal = aumid.Contains("tidal", StringComparison.OrdinalIgnoreCase);

    Console.WriteLine($"[{index}] AUMID: {aumid}{(looksLikeTidal ? "   <-- looks like TIDAL" : "")}");

    // Playback info (status, controls) is synchronous.
    try
    {
        var playback = session.GetPlaybackInfo();
        Console.WriteLine($"     PlaybackStatus : {playback.PlaybackStatus}");
        Console.WriteLine($"     PlaybackType   : {playback.PlaybackType}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"     PlaybackInfo   : <error: {ex.Message}>");
    }

    // Media properties (title/artist/album/thumbnail) require an async call.
    try
    {
        var props = await session.TryGetMediaPropertiesAsync();
        if (props is not null)
        {
            Console.WriteLine($"     Title          : {props.Title}");
            Console.WriteLine($"     Artist         : {props.Artist}");
            Console.WriteLine($"     AlbumTitle     : {props.AlbumTitle}");
            Console.WriteLine($"     AlbumArtist    : {props.AlbumArtist}");
            Console.WriteLine($"     TrackNumber    : {props.TrackNumber}");
            Console.WriteLine($"     Genres         : {string.Join(", ", props.Genres)}");
            Console.WriteLine($"     Thumbnail?     : {(props.Thumbnail is null ? "no" : "yes (IRandomAccessStreamReference)")}");
        }
        else
        {
            Console.WriteLine("     MediaProperties: <null>");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"     MediaProperties: <error: {ex.Message}>");
    }

    // Timeline properties (position / start / end) drive the Discord progress bar.
    try
    {
        var t = session.GetTimelineProperties();
        Console.WriteLine($"     Position       : {t.Position}");
        Console.WriteLine($"     StartTime      : {t.StartTime}");
        Console.WriteLine($"     EndTime        : {t.EndTime}");
        Console.WriteLine($"     MinSeekTime    : {t.MinSeekTime}");
        Console.WriteLine($"     MaxSeekTime    : {t.MaxSeekTime}");
        Console.WriteLine($"     LastUpdated    : {t.LastUpdatedTime}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"     Timeline       : <error: {ex.Message}>");
    }

    Console.WriteLine();
}

if (sessions.Count == 0)
{
    Console.WriteLine("No SMTC sessions. Make sure Tidal is open and has played a track.");
}

Console.WriteLine("Done. Copy the exact TIDAL AUMID above so we can hard-match it.");
