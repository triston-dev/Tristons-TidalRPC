// ---------------------------------------------------------------------------
//  Triston's TidalRPC
//  Discord Rich Presence for the Tidal desktop app (via Windows SMTC).
//
//  Author : triston-dev  <https://github.com/triston-dev>
//  Product: Triston's TidalRPC
//  License: MIT
//
//  AlbumArtResolver.cs — Discord wants an image URL (or an uploaded asset key),
//  but SMTC only hands us a raw thumbnail stream. So we resolve a *hosted*
//  cover-art URL by querying public catalog APIs (iTunes Search, then Deezer)
//  using artist + title. Results are cached per track. Note: Tidal reports an
//  empty album over SMTC, so we deliberately key on artist + title.
// ---------------------------------------------------------------------------

using System.Net.Http;
using System.Text.Json;

namespace TristonsTidalRPC;

/// <summary>Resolves a hosted album-art URL for a track, with an in-memory cache.</summary>
internal sealed class AlbumArtResolver : IDisposable
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, string?> _cache = new();
    private readonly object _cacheGate = new();

    public AlbumArtResolver()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TristonsTidalRPC/1.0 (+https://github.com/triston-dev)");
    }

    /// <summary>
    /// Returns a hosted image URL for the track, or <c>null</c> if nothing was
    /// found (caller should fall back to the bundled default asset). Cached by
    /// <see cref="TrackInfo.IdentityKey"/> so we never re-query per SMTC event.
    /// </summary>
    public async Task<string?> ResolveAsync(TrackInfo track)
    {
        if (string.IsNullOrWhiteSpace(track.Title))
            return null;

        string key = track.IdentityKey;
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;
        }

        string? url = null;
        try
        {
            url = await QueryItunesAsync(track).ConfigureAwait(false)
                  ?? await QueryDeezerAsync(track).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("Album-art lookup failed", ex);
        }

        lock (_cacheGate) { _cache[key] = url; }
        Logger.Debug($"Album art for '{track.Artist} - {track.Title}': {url ?? "<none>"}");
        return url;
    }

    // iTunes Search API — no key required. artworkUrl100 is a 100px thumbnail
    // whose size is embedded in the URL, so we swap it up to 600px.
    private async Task<string?> QueryItunesAsync(TrackInfo track)
    {
        string term = Uri.EscapeDataString($"{track.Artist} {track.Title}".Trim());
        string endpoint = $"https://itunes.apple.com/search?term={term}&entity=song&limit=1";

        using var doc = JsonDocument.Parse(await _http.GetStringAsync(endpoint).ConfigureAwait(false));
        var results = doc.RootElement.GetProperty("results");
        if (results.GetArrayLength() == 0) return null;

        string? art = results[0].TryGetProperty("artworkUrl100", out var a) ? a.GetString() : null;
        return art?.Replace("100x100bb", "600x600bb");
    }

    // Deezer public search — no key required. Prefer the largest cover available.
    private async Task<string?> QueryDeezerAsync(TrackInfo track)
    {
        string q = Uri.EscapeDataString($"{track.Artist} {track.Title}".Trim());
        string endpoint = $"https://api.deezer.com/search?q={q}&limit=1";

        using var doc = JsonDocument.Parse(await _http.GetStringAsync(endpoint).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            return null;

        var album = data[0].GetProperty("album");
        foreach (var field in new[] { "cover_xl", "cover_big", "cover_medium", "cover" })
        {
            if (album.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String)
            {
                string? url = v.GetString();
                if (!string.IsNullOrEmpty(url)) return url;
            }
        }
        return null;
    }

    public void Dispose() => _http.Dispose();
}
