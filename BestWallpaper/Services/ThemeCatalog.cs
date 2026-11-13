using System.Text.Json.Serialization;
using BestWallpaper.Models;

namespace BestWallpaper.Services;

/// <summary>内置中文词库，以及"内置 + 覆盖 + 停用 + 自定义"的合并规则。</summary>
public static class ThemeCatalog
{
    public sealed class Direction
    {
        public string Name { get; }
        public IReadOnlyList<(string Display, string Query)> Words { get; }
        public Direction(string name, IReadOnlyList<(string, string)> words) { Name = name; Words = words; }
    }

    public const string CustomGroup = "自定义";

    public static IReadOnlyList<Direction> Directions { get; } = new List<Direction>
    {
        new("自然风景", new (string, string)[]
        {
            ("山", "mountains"), ("海", "ocean"), ("森林", "forest"), ("星空", "starry sky"), ("云", "clouds"),
        }),
        new("萌宠", new (string, string)[]
        {
            ("猫", "cat"), ("狗", "dog"), ("兔", "rabbit"), ("狐狸", "fox"), ("小动物", "animal"),
        }),
        new("游戏", new (string, string)[]
        {
            ("游戏", "video game"), ("游戏原画", "game art"), ("像素风", "pixel art"), ("奇幻", "fantasy"),
        }),
        new("极简抽象", new (string, string)[]
        {
            ("极简", "minimalism"), ("抽象", "abstract"), ("几何", "geometry"), ("渐变", "gradient"),
        }),
        new("城市赛博朋克", new (string, string)[]
        {
            ("城市", "cityscape"), ("城市夜景", "night city"), ("赛博朋克", "cyberpunk"), ("霓虹", "neon"),
        }),
        new("太空科幻", new (string, string)[]
        {
            ("太空", "space"), ("星云", "nebula"), ("科幻", "sci fi"), ("星球", "planet"),
        }),
        new("汽车机械", new (string, string)[]
        {
            ("汽车", "car"), ("摩托", "motorcycle"), ("机械", "mechanical"), ("飞机", "aircraft"),
        }),
    };

    /// <summary>管理界面用：内置（含停用、含覆盖）+ 自定义，不做启用过滤。</summary>
    public static List<ThemeWord> All(AppConfig cfg)
    {
        var result = new List<ThemeWord>();
        foreach (var d in Directions)
        {
            var directionOff = cfg.DisabledDirections.Contains(d.Name);
            foreach (var (display, query) in d.Words)
            {
                var overridden = cfg.ThemeOverrides.FirstOrDefault(o =>
                    string.Equals(o.OriginalQuery, query, StringComparison.OrdinalIgnoreCase));

                result.Add(new ThemeWord
                {
                    Display = overridden is not null && !string.IsNullOrWhiteSpace(overridden.Display) ? overridden.Display : display,
                    Query = overridden is not null && !string.IsNullOrWhiteSpace(overridden.Query) ? overridden.Query : query,
                    Direction = d.Name,
                    BuiltIn = true,
                    OriginalQuery = query,
                    Modified = overridden is not null,
                    Verified = true,
                    Enabled = !directionOff && !IsWordDisabled(cfg, query),
                });
            }
        }

        foreach (var custom in cfg.CustomThemes)
        {
            custom.BuiltIn = false;
            custom.Direction = CustomGroup;
            custom.Enabled = !IsWordDisabled(cfg, custom.Query);
            result.Add(custom);
        }

        return result;
    }

    /// <summary>抽取用的主题池：只含启用项，按搜索标签去重（内置优先）。</summary>
    public static List<ThemeWord> Pool(AppConfig cfg)
    {
        var pool = new List<ThemeWord>();
        foreach (var word in All(cfg))
        {
            if (!word.Enabled) continue;
            if (string.IsNullOrWhiteSpace(word.Display) || string.IsNullOrWhiteSpace(word.Query)) continue;
            if (pool.Any(w => string.Equals(w.Query, word.Query, StringComparison.OrdinalIgnoreCase))) continue;
            pool.Add(word);
        }
        return pool;
    }

    /// <summary>按显示名或搜索标签查找（大小写、首尾空格不敏感）。</summary>
    public static ThemeWord? Find(AppConfig cfg, string text)
    {
        var key = (text ?? "").Trim();
        if (key.Length == 0) return null;
        var all = All(cfg);
        return all.FirstOrDefault(w => string.Equals(w.Display, key, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(w => string.Equals(w.Query, key, StringComparison.OrdinalIgnoreCase));
    }

    public static ThemeWord? PickRandom(AppConfig cfg, Random rng)
    {
        var pool = Pool(cfg);
        if (pool.Count == 0) return null;
        return pool[rng.Next(pool.Count)];
    }

    public static ThemeWord? ResolveFixed(AppConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.FixedThemeDisplay) && string.IsNullOrWhiteSpace(cfg.FixedThemeQuery)) return null;
        var pool = Pool(cfg);
        return pool.FirstOrDefault(w => string.Equals(w.Display, cfg.FixedThemeDisplay, StringComparison.Ordinal))
               ?? pool.FirstOrDefault(w => string.Equals(w.Query, cfg.FixedThemeQuery, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>今日主题：只有日期匹配今天才生效，优先于固定主题。</summary>
    public static ThemeWord? ResolveToday(AppConfig cfg, string today)
    {
        if (!string.Equals(cfg.TodayThemeDate, today, StringComparison.Ordinal)) return null;
        if (string.IsNullOrWhiteSpace(cfg.TodayThemeQuery)) return null;
        return new ThemeWord
        {
            Display = string.IsNullOrWhiteSpace(cfg.TodayThemeDisplay) ? cfg.TodayThemeQuery : cfg.TodayThemeDisplay,
            Query = cfg.TodayThemeQuery,
            Direction = "",
            BuiltIn = false,
            Verified = true,
            Enabled = true,
        };
    }

    public static bool IsWordDisabled(AppConfig cfg, string queryOrOriginal)
        => cfg.DisabledWords.Any(w => string.Equals(w, queryOrOriginal, StringComparison.OrdinalIgnoreCase));
}
