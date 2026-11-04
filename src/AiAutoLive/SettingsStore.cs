using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AiAutoLive;

/// Keys and IDs entered on the settings page. Saved encrypted (ASP.NET Data Protection, DPAPI on Windows)
/// outside the repo; a saved value overrides the same key from appsettings.json or environment variables.
public sealed class SettingsStore(string path, IDataProtector protector)
{
    public static readonly string[] SecretNames =
        [nameof(LiveOptions.OpenAIApiKey), nameof(LiveOptions.ElevenLabsApiKey), nameof(LiveOptions.PageAccessToken)];
    public static readonly string[] Names = [.. SecretNames, nameof(LiveOptions.PageId), nameof(LiveOptions.ElevenLabsVoiceId)];
    const int MaxLength = 2000;

    readonly Lock gate = new();
    readonly Dictionary<string, string> values = Load(path, protector);

    /// Blank or null values keep what is saved. Returns an error message and saves nothing if any entry is invalid.
    public string? Save(IReadOnlyDictionary<string, string?> updates)
    {
        foreach (var (name, value) in updates)
        {
            if (!Names.Contains(name)) return $"Không có mục cài đặt '{name}'";
            if (value?.Trim().Length > MaxLength) return $"{name} dài quá {MaxLength} ký tự";
        }

        lock (gate)
        {
            foreach (var (name, value) in updates)
                if (!string.IsNullOrWhiteSpace(value)) values[name] = value.Trim();

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Write then rename, so a crash mid-write never leaves a half file that would lose every saved key.
            File.WriteAllText(path + ".tmp", protector.Protect(JsonSerializer.Serialize(values)));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        return null;
    }

    public void ApplyTo(LiveOptions o)
    {
        lock (gate)
        {
            o.OpenAIApiKey = Pick(nameof(o.OpenAIApiKey), o.OpenAIApiKey);
            o.ElevenLabsApiKey = Pick(nameof(o.ElevenLabsApiKey), o.ElevenLabsApiKey);
            o.PageAccessToken = Pick(nameof(o.PageAccessToken), o.PageAccessToken);
            o.PageId = Pick(nameof(o.PageId), o.PageId);
            o.ElevenLabsVoiceId = Pick(nameof(o.ElevenLabsVoiceId), o.ElevenLabsVoiceId);
        }
    }

    /// What the settings page may show of a secret: never more than its last four characters.
    public static string Mask(string value) => value.Length switch
    {
        0 => "",
        <= 8 => "••••",
        _ => "…" + value[^4..],
    };

    string Pick(string name, string current) => values.TryGetValue(name, out var v) ? v : current;

    static Dictionary<string, string> Load(string path, IDataProtector protector)
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(protector.Unprotect(File.ReadAllText(path))) ?? [];
        }
        catch (CryptographicException)
        {
            // Encrypted for another Windows account or machine: start empty, the user enters the keys again.
            return [];
        }
    }
}
