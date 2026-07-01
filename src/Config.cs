// ---------------------------------------------------------------------------
//  Triston's TidalRPC
//  Discord Rich Presence for the Tidal desktop app (via Windows SMTC).
//
//  Author : triston-dev  <https://github.com/triston-dev>
//  Product: Triston's TidalRPC
//  License: MIT
//
//  Config.cs — user toggles persisted as JSON in
//  %APPDATA%\Tristons TidalRPC\config.json.
// ---------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TristonsTidalRPC;

/// <summary>Persisted user preferences. All toggles survive between runs.</summary>
internal sealed class Config
{
    public bool RpcEnabled { get; set; } = true;
    public bool HideAds { get; set; } = false;
    public bool RunAtStartup { get; set; } = false;

    /// <summary>Verbose (DEBUG) logging switch.</summary>
    public bool Verbose { get; set; } = false;

    [JsonIgnore]
    public string FilePath { get; private set; } = "";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Config Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(path)) ?? new Config();
                cfg.FilePath = path;
                return cfg;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load config; using defaults", ex);
        }

        var fresh = new Config { FilePath = path };
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save config", ex);
        }
    }
}
