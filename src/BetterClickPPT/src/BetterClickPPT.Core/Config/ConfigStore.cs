using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.Core.Config;

/// <summary>配置文件读写。</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _filePath;
    private readonly AppLog? _log;

    public ConfigStore(string? filePath = null, AppLog? log = null)
    {
        _filePath = filePath ?? AppPaths.ConfigFile;
        _log = log;
    }

    /// <summary>配置文件路径。</summary>
    public string FilePath => _filePath;

    /// <summary>读取配置；文件不存在或损坏时返回默认配置。</summary>
    public AppConfig Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
                if (config is not null)
                {
                    return config.Normalize();
                }
            }
        }
        catch (Exception ex)
        {
            _log?.Warn($"读取配置失败，将使用默认配置：{ex.Message}");
        }

        return AppConfig.CreateDefault().Normalize();
    }

    /// <summary>保存配置（原子写入）。</summary>
    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Normalize();

        AppPaths.EnsureDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, json);

        if (File.Exists(_filePath))
        {
            File.Replace(temp, _filePath, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, _filePath);
        }
    }

    /// <summary>删除配置文件（卸载时使用）。</summary>
    public void DeleteIfExists()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        catch (Exception ex)
        {
            _log?.Warn($"删除配置失败：{ex.Message}");
        }
    }
}
