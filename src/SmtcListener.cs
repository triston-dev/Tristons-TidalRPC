// ---------------------------------------------------------------------------
//  Triston's TidalRPC
//  Discord Rich Presence for the Tidal desktop app (via Windows SMTC).
//
//  Author : triston-dev  <https://github.com/triston-dev>
//  Product: Triston's TidalRPC
//  License: MIT
//
//  SmtcListener.cs — subscribes to the Windows System Media Transport Controls
//  (SMTC), locates the Tidal session *by its AUMID* (so other media apps can't
//  hijack the presence), and raises a debounced TrackChanged event.
// ---------------------------------------------------------------------------

using Windows.Media.Control;

namespace TristonsTidalRPC;

using GsmtcManager = GlobalSystemMediaTransportControlsSessionManager;
using GsmtcSession = GlobalSystemMediaTransportControlsSession;
using PlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus;

/// <summary>
/// Watches SMTC for Tidal and reports track changes. All session mutation and
/// event raising is marshalled onto the WinForms UI thread via the captured
/// <see cref="SynchronizationContext"/>, so consumers never touch WinRT
/// objects from a thread-pool callback.
/// </summary>
internal sealed class SmtcListener : IDisposable
{
    /// <summary>
    /// Tidal's SMTC identity, confirmed on this machine via the startup probe.
    /// Tidal ships as a Squirrel/Electron package, hence the com.squirrel.* id.
    /// </summary>
    public const string TidalAumid = "com.squirrel.TIDAL.TIDAL";

    private readonly SynchronizationContext _ui;
    private readonly System.Windows.Forms.Timer _debounce;

    private GsmtcManager? _manager;
    private GsmtcSession? _tidal;
    private bool _disposed;

    /// <summary>Fires with the current track, or <c>null</c> when Tidal is gone / stopped / closed.</summary>
    public event Action<TrackInfo?>? TrackChanged;

    /// <summary>Fires whenever the set of SMTC sessions is (re)enumerated, for AUMID logging in the UI.</summary>
    public event Action<IReadOnlyList<string>>? SessionsEnumerated;

    public SmtcListener(SynchronizationContext uiContext)
    {
        _ui = uiContext;

        // A WinForms timer ticks on the UI thread; we use it to coalesce bursts
        // of SMTC events (rapid skips / seeks) into a single presence update.
        _debounce = new System.Windows.Forms.Timer { Interval = 350 };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = PublishCurrentAsync();
        };
    }

    /// <summary>Ask Windows for the session manager and attach to Tidal (if present).</summary>
    public async Task InitializeAsync()
    {
        _manager = await GsmtcManager.RequestAsync();
        _manager.SessionsChanged += OnSessionsChanged;
        AttachTidalSession(); // runs on UI thread (continuation of the await above)
    }

    /// <summary>Force a fresh read + publish (e.g. after the user re-enables RPC).</summary>
    public void RequestRefresh() => RequestDebouncedUpdate();

    // ----- session (re)selection -------------------------------------------

    private void OnSessionsChanged(GsmtcManager sender, SessionsChangedEventArgs args)
    {
        // This callback arrives on a thread-pool thread. Marshal all session
        // handling to the UI thread so _tidal is only ever touched there.
        Post(AttachTidalSession);
    }

    private void AttachTidalSession()
    {
        if (_manager is null || _disposed) return;

        var sessions = _manager.GetSessions();
        var aumids = sessions.Select(s => s.SourceAppUserModelId).ToList();

        // Requirement: log every session's AUMID so the exact match string is verifiable.
        Logger.Info("SMTC sessions: " + (aumids.Count == 0 ? "<none>" : string.Join(" | ", aumids)));
        SessionsEnumerated?.Invoke(aumids);

        var match = sessions.FirstOrDefault(s => IsTidal(s.SourceAppUserModelId));

        // Re-wire handlers: drop the old Tidal session, attach to the new one.
        if (_tidal is not null)
        {
            DetachHandlers(_tidal);
            _tidal = null;
        }

        if (match is not null)
        {
            _tidal = match;
            AttachHandlers(_tidal);
            Logger.Info($"Attached to Tidal SMTC session: {match.SourceAppUserModelId}");
            RequestDebouncedUpdate();
        }
        else
        {
            Logger.Info("No Tidal SMTC session present — clearing presence.");
            TrackChanged?.Invoke(null);
        }
    }

    private static bool IsTidal(string aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return false;
        if (string.Equals(aumid, TidalAumid, StringComparison.OrdinalIgnoreCase)) return true;
        // Fallback so other Tidal install/packaging variants still match.
        return aumid.Contains("tidal", StringComparison.OrdinalIgnoreCase);
    }

    // ----- per-session change events ---------------------------------------

    private void AttachHandlers(GsmtcSession s)
    {
        s.MediaPropertiesChanged += OnSessionChanged;
        s.PlaybackInfoChanged += OnSessionChanged;
        s.TimelinePropertiesChanged += OnSessionChanged;
    }

    private void DetachHandlers(GsmtcSession s)
    {
        s.MediaPropertiesChanged -= OnSessionChanged;
        s.PlaybackInfoChanged -= OnSessionChanged;
        s.TimelinePropertiesChanged -= OnSessionChanged;
    }

    // A single handler shape covers all three event types via overloads.
    private void OnSessionChanged(GsmtcSession s, MediaPropertiesChangedEventArgs a) => RequestDebouncedUpdate();
    private void OnSessionChanged(GsmtcSession s, PlaybackInfoChangedEventArgs a) => RequestDebouncedUpdate();
    private void OnSessionChanged(GsmtcSession s, TimelinePropertiesChangedEventArgs a) => RequestDebouncedUpdate();

    private void RequestDebouncedUpdate()
    {
        // (Re)start the debounce timer on the UI thread.
        Post(() =>
        {
            if (_disposed) return;
            _debounce.Stop();
            _debounce.Start();
        });
    }

    // ----- read current state and publish ----------------------------------

    private async Task PublishCurrentAsync()
    {
        var session = _tidal;
        if (session is null)
        {
            TrackChanged?.Invoke(null);
            return;
        }

        try
        {
            var playback = session.GetPlaybackInfo();
            var status = playback.PlaybackStatus;

            // Stopped/Closed => nothing meaningful to show; clear presence.
            if (status is PlaybackStatus.Stopped or PlaybackStatus.Closed)
            {
                TrackChanged?.Invoke(null);
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync(); // resumes on UI thread
            var timeline = session.GetTimelineProperties();

            var info = new TrackInfo
            {
                Title = props?.Title ?? "",
                Artist = props?.Artist ?? "",
                // Tidal reports AlbumTitle empty over SMTC in practice; keep it
                // anyway in case a future build populates it.
                Album = props?.AlbumTitle ?? "",
                Status = status,
                Position = timeline.Position,
                StartTime = timeline.StartTime,
                EndTime = timeline.EndTime,
                LastUpdated = timeline.LastUpdatedTime,
            };

            Logger.Debug($"Publish: {info}");
            TrackChanged?.Invoke(info);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to read SMTC state", ex);
        }
    }

    // ----- helpers ----------------------------------------------------------

    private void Post(Action action) => _ui.Post(_ => action(), null);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_manager is not null) _manager.SessionsChanged -= OnSessionsChanged;
            if (_tidal is not null) DetachHandlers(_tidal);
        }
        catch (Exception ex)
        {
            Logger.Error("Error during SmtcListener dispose", ex);
        }

        _debounce.Dispose();
        _tidal = null;
        _manager = null;
    }
}
