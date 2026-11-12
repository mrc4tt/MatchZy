using System.Text.Json;
using CounterStrikeSharp.API.Core;

namespace MatchZy;

/// <summary>
/// Gamedata lookup that survives a gamedata update without a server restart. CounterStrikeSharp
/// reads addons/counterstrikesharp/gamedata/*.json once when it starts, so after deploying a new
/// matchzy.json with a new key, a plugin reload still gets "key missing" from GameData until the
/// server restarts. Every MatchZy lookup goes through here: GameData first, then MatchZy's own
/// gamedata file read from disk.
/// </summary>
public static class MzGameData
{
    // Set from Load (ModuleDirectory = .../addons/counterstrikesharp/plugins/MatchZy).
    public static string? FilePath { get; private set; }

    public static void Init(string moduleDirectory)
    {
        FilePath = Path.GetFullPath(Path.Join(moduleDirectory, "..", "..", "gamedata", "matchzy.json"));
    }

    private static string PlatformKey => OperatingSystem.IsWindows() ? "windows" : "linux";

    /// <exception cref="InvalidOperationException">Neither CSS nor matchzy.json has the key.</exception>
    public static string GetSignature(string key)
    {
        Exception? cssError = null;
        try
        {
            string sig = GameData.GetSignature(key);
            if (!string.IsNullOrWhiteSpace(sig))
                return sig;
        }
        catch (Exception e)
        {
            cssError = e;
        }
        if (ReadFromFile(key, "signatures") is { ValueKind: JsonValueKind.String } v && !string.IsNullOrWhiteSpace(v.GetString()))
            return v.GetString()!;
        throw new InvalidOperationException($"gamedata key {key} ({PlatformKey} signature) is not in CounterStrikeSharp's gamedata nor in {FilePath ?? "matchzy.json"}", cssError);
    }

    /// <exception cref="InvalidOperationException">Neither CSS nor matchzy.json has the key.</exception>
    public static int GetOffset(string key)
    {
        Exception? cssError = null;
        try
        {
            return GameData.GetOffset(key);
        }
        catch (Exception e)
        {
            cssError = e;
        }
        if (ReadFromFile(key, "offsets") is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out int off))
            return off;
        throw new InvalidOperationException($"gamedata key {key} ({PlatformKey} offset) is not in CounterStrikeSharp's gamedata nor in {FilePath ?? "matchzy.json"}", cssError);
    }

    private static JsonElement? ReadFromFile(string key, string section)
    {
        try
        {
            if (FilePath == null || !File.Exists(FilePath))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.TryGetProperty(key, out var entry)
                && entry.TryGetProperty(section, out var sec)
                && sec.TryGetProperty(PlatformKey, out var value))
                return value.Clone();
        }
        catch { }
        return null;
    }
}
