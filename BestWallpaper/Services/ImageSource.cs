namespace BestWallpaper.Services;

/// <summary>图片源返回的一条候选。RemoteUrls 按优先级排列（Bing 有 UHD → 1080 两档）。</summary>
public sealed record SourceItem(
    string Id,
    string Source,
    IReadOnlyList<string> RemoteUrls,
    int Width,
    int Height,
    long Bytes,
    string? PageUrl,
    string? Author);

public interface IImageSource
{
    string Name { get; }

    /// <summary>highRes=true 时只请求 ≥2560×1440，false 时放宽到 ≥1920×1080。</summary>
    Task<IReadOnlyList<SourceItem>> SearchAsync(HttpClient http, string query, int want, bool highRes, CancellationToken ct);
}
