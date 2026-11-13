using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BestWallpaper.Models;

namespace BestWallpaper.Services;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(Paths.ConfigFile))
            {
                Log.Info("config 不存在，使用默认配置");
                return new AppConfig();
            }

            var json = File.ReadAllText(Paths.ConfigFile);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, ReadOptions);
            if (cfg is null)
            {
                Log.Warn("config 反序列化为 null，使用默认配置");
                return new AppConfig();
            }

            Normalize(cfg);
            return cfg;
        }
        catch (Exception ex)
        {
            Log.Error("读取 config 失败，改用默认配置（原文件保留为 .bak）", ex);
            try
            {
                if (File.Exists(Paths.ConfigFile))
                    File.Copy(Paths.ConfigFile, Paths.ConfigFile + ".bak", overwrite: true);
            }
            catch { }
            return new AppConfig();
        }
    }

    private static void Normalize(AppConfig cfg)
    {
        cfg.RotationMinutes = Math.Clamp(cfg.RotationMinutes, 1, 1440);
        cfg.MaxPoolDays = Math.Clamp(cfg.MaxPoolDays, 1, 60);
        if (cfg.MaxCacheBytes <= 0) cfg.MaxCacheBytes = 2L * 1024 * 1024 * 1024;
        cfg.CustomThemes ??= new List<ThemeWord>();
        cfg.Excluded ??= new List<string>();
        cfg.Favorites ??= new List<FavoriteEntry>();
        cfg.RecentIds ??= new List<string>();
        cfg.DisabledDirections ??= new List<string>();
        cfg.DisabledWords ??= new List<string>();
        cfg.ThemeOverrides ??= new List<ThemeOverride>();
        cfg.TodayThemeDate ??= "";
        cfg.TodayThemeDisplay ??= "";
        cfg.TodayThemeQuery ??= "";
    }

    /// <summary>原子写入：先写 .tmp 再替换，避免断电留下半截文件。</summary>
    public static void Save(AppConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(Paths.Root);
            var tmp = Paths.ConfigFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, Options), new System.Text.UTF8Encoding(false));
            File.Move(tmp, Paths.ConfigFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("保存 config 失败", ex);
        }
    }

    public static string Serialize(AppConfig cfg) => JsonSerializer.Serialize(cfg, Options);

    public static DailyPool? LoadPool(string date)
    {
        try
        {
            var file = Paths.MetaFile(date);
            if (!File.Exists(file)) return null;
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<DailyPool>(json, ReadOptions);
        }
        catch (Exception ex)
        {
            Log.Error($"读取池子 meta 失败: {date}", ex);
            return null;
        }
    }

    public static void SavePool(DailyPool pool)
    {
        try
        {
            Directory.CreateDirectory(Paths.PoolDir(pool.Date));
            var file = Paths.MetaFile(pool.Date);
            var tmp = file + ".tmp";
            var json = JsonSerializer.Serialize(pool, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            File.WriteAllText(tmp, json, new System.Text.UTF8Encoding(false));
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"保存池子 meta 失败: {pool.Date}", ex);
        }
    }
}
