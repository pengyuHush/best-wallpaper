namespace BestWallpaper.Models;

public enum ThemeSource
{
    DailyRandom,
    Fixed
}

public enum ProxyMode
{
    System,
    Direct,
    Manual
}

public sealed class ThemeWord
{
    public string Display { get; set; } = "";
    public string Query { get; set; } = "";
    public string Direction { get; set; } = "";
    public bool BuiltIn { get; set; }

    /// <summary>自定义词是否通过试搜验证；内置词恒为 true。</summary>
    public bool Verified { get; set; } = true;

    /// <summary>被覆盖修改的内置词的原始标签（用于"恢复默认"）。</summary>
    public string? OriginalQuery { get; set; }

    /// <summary>是否被用户覆盖修改过。</summary>
    public bool Modified { get; set; }

    /// <summary>是否参与抽取（由停用方向/停用词推算，不入配置）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Enabled { get; set; } = true;
}

/// <summary>内置主题的覆盖修改：按原始搜索标签定位，可改显示名与标签。</summary>
public sealed class ThemeOverride
{
    public string OriginalQuery { get; set; } = "";
    public string Display { get; set; } = "";
    public string Query { get; set; } = "";
}

public sealed class FavoriteEntry
{
    public string Id { get; set; } = "";
    public string File { get; set; } = "";
    public string Theme { get; set; } = "";
    public string AddedAt { get; set; } = "";
    public string? PageUrl { get; set; }
}

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;

    // 主题
    public ThemeSource ThemeMode { get; set; } = ThemeSource.DailyRandom;
    public string FixedThemeDisplay { get; set; } = "";
    public string FixedThemeQuery { get; set; } = "";
    public List<ThemeWord> CustomThemes { get; set; } = new();
    public List<string> DisabledDirections { get; set; } = new();

    /// <summary>停用的单个内置主题，按原始搜索标签记录。</summary>
    public List<string> DisabledWords { get; set; } = new();

    /// <summary>对内置主题的覆盖修改。</summary>
    public List<ThemeOverride> ThemeOverrides { get; set; } = new();

    /// <summary>今日主题：只对 TodayThemeDate 这一天生效，优先于固定主题。</summary>
    public string TodayThemeDate { get; set; } = "";
    public string TodayThemeDisplay { get; set; } = "";
    public string TodayThemeQuery { get; set; } = "";

    // 轮播
    public int RotationMinutes { get; set; } = 10;
    public bool Paused { get; set; }
    public string? PausedReason { get; set; }

    // 今日池状态
    public string? LastPoolDate { get; set; }
    public string? LastThemeDisplay { get; set; }
    public int CurrentIndex { get; set; } = -1;
    public string? LastSetPath { get; set; }

    // 网络
    public ProxyMode ProxyMode { get; set; } = ProxyMode.System;
    public string ProxyAddress { get; set; } = "";

    // 启动
    public bool Autostart { get; set; }
    public int StartDelaySeconds { get; set; } = 60;

    // 收藏 / 排除 / 去重
    public List<string> Excluded { get; set; } = new();
    public List<FavoriteEntry> Favorites { get; set; } = new();
    public List<string> RecentIds { get; set; } = new();

    // 观测
    public bool Notifications { get; set; } = true;
    public string? LastRefreshAt { get; set; }
    public bool LastRefreshOk { get; set; }
    public string? LastError { get; set; }

    // 存储上限
    public int MaxPoolDays { get; set; } = 7;
    public long MaxCacheBytes { get; set; } = 2L * 1024 * 1024 * 1024;
}
