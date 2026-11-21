using System.Text;
using System.Text.Json;

namespace MarkTop.Core.Configuration;

/// <summary>Writes a file by writing a sibling temp file and moving it over the target.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string content, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, content, encoding);
            // File.Move with overwrite is atomic on the same volume.
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }
    }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private AppSettings _settings;

    public SettingsStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(AppContext.BaseDirectory, "setting-marktop.json");
        _settings = Load(out var warning);
        LoadWarning = warning;
    }

    public string Path => _path;

    public AppSettings Settings => _settings;

    /// <summary>Non-null when the file existed but could not be parsed and a backup was kept.</summary>
    public string? LoadWarning { get; private set; }

    /// <summary>True when the settings directory rejected writes (e.g. Program Files).</summary>
    public bool IsReadOnly { get; private set; }

    public AppSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_path, new UTF8Encoding(false, false));
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null)
            {
                throw new JsonException("empty document");
            }

            return settings.Normalize();
        }
        catch (Exception ex)
        {
            var backup = $"{_path}.bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Copy(_path, backup, overwrite: false);
                warning = $"配置文件无法解析（{ex.Message}），已备份为 {System.IO.Path.GetFileName(backup)} 并使用默认配置。";
            }
            catch
            {
                warning = $"配置文件无法解析（{ex.Message}），已使用默认配置。";
            }

            return new AppSettings();
        }
    }

    public void Reload() => _settings = Load(out _);

    /// <summary>Persists settings; returns false when the directory is not writable.</summary>
    public bool Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            AtomicFile.WriteAllText(_path, json + Environment.NewLine, new UTF8Encoding(false));
            IsReadOnly = false;
            return true;
        }
        catch (Exception)
        {
            IsReadOnly = true;
            return false;
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        mutate(_settings);
        _settings.Normalize();
        Save();
    }
}
