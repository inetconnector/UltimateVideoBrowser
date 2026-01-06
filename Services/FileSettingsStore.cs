using System.Globalization;
using System.Text.Json;
using UltimateVideoBrowser.Helpers;

namespace UltimateVideoBrowser.Services;

public sealed class FileSettingsStore
{
    private readonly object gate = new();
    private bool loaded;
    private Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public FileSettingsStore(string? settingsPath = null)
    {
        this.SettingsPath = settingsPath ?? Path.Combine(AppDataPaths.Root, "settings.json");
        var directory = Path.GetDirectoryName(this.SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    public string SettingsPath { get; }

    public void ReloadFromDisk()
    {
        lock (gate)
        {
            loaded = false;
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            EnsureLoaded();
        }
    }

    public void ReplaceFromFile(string sourceSettingsPath)
    {
        if (string.IsNullOrWhiteSpace(sourceSettingsPath) || !File.Exists(sourceSettingsPath))
            throw new FileNotFoundException("Source settings file not found.", sourceSettingsPath);

        lock (gate)
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.Copy(sourceSettingsPath, SettingsPath, true);
            loaded = false;
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            EnsureLoaded();
        }
    }

    public string GetString(string key, string fallback)
    {
        lock (gate)
        {
            EnsureLoaded();
            return values.TryGetValue(key, out var value) ? value : fallback;
        }
    }

    public void SetString(string key, string? value)
    {
        lock (gate)
        {
            EnsureLoaded();
            values[key] = value ?? string.Empty;
            Persist();
        }
    }

    public bool GetBool(string key, bool fallback)
    {
        lock (gate)
        {
            EnsureLoaded();
            if (values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed))
                return parsed;
            return fallback;
        }
    }

    public void SetBool(string key, bool value)
    {
        lock (gate)
        {
            EnsureLoaded();
            values[key] = value.ToString(CultureInfo.InvariantCulture);
            Persist();
        }
    }

    public int GetInt(string key, int fallback)
    {
        lock (gate)
        {
            EnsureLoaded();
            if (values.TryGetValue(key, out var value)
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return fallback;
        }
    }

    public void SetInt(string key, int value)
    {
        lock (gate)
        {
            EnsureLoaded();
            values[key] = value.ToString(CultureInfo.InvariantCulture);
            Persist();
        }
    }

    public long GetLong(string key, long fallback)
    {
        lock (gate)
        {
            EnsureLoaded();
            if (values.TryGetValue(key, out var value)
                && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return fallback;
        }
    }

    public void SetLong(string key, long value)
    {
        lock (gate)
        {
            EnsureLoaded();
            values[key] = value.ToString(CultureInfo.InvariantCulture);
            Persist();
        }
    }

    public bool ContainsKey(string key)
    {
        lock (gate)
        {
            EnsureLoaded();
            return values.ContainsKey(key);
        }
    }

    private void EnsureLoaded()
    {
        if (loaded)
            return;

        if (File.Exists(SettingsPath))
            try
            {
                var json = File.ReadAllText(SettingsPath);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (parsed != null)
                    values = new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

        loaded = true;
    }

    private void Persist()
    {
        var tempPath = $"{SettingsPath}.tmp";
        var json = JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, SettingsPath, true);
    }
}