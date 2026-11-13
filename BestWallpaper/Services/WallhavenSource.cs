using System.Net;
using System.Text;
using System.Text.Json;

namespace BestWallpaper.Services;

/// <summary>
/// wallhaven API v1：免 key、45 次/分钟、返回原图。
/// 主源。注意本机直连被 DNS 污染，必须走代理。
/// </summary>
public sealed class WallhavenSource : IImageSource
{
    public string Name => "wallhaven";

    private const string BaseUrl = "https://wallhaven.cc/api/v1/search";
    private static readonly Random Rng = new();

    public async Task<IReadOnlyList<SourceItem>> SearchAsync(HttpClient http, string query, int want, bool highRes, CancellationToken ct)
    {
        var seed = RandomSeed();
        var sb = new StringBuilder(BaseUrl);
        sb.Append("?q=").Append(Uri.EscapeDataString(query));
        sb.Append("&categories=100");   // general only：避开 anime / people
        sb.Append("&purity=100");       // SFW only
        sb.Append("&sorting=random");
        sb.Append("&seed=").Append(seed);

        if (highRes)
        {
            // atleast 只接受 1920x1080/1920x1200，要高分辨率必须用精确 resolutions
            sb.Append("&resolutions=2560x1440,3840x2160");
        }
        else
        {
            sb.Append("&atleast=1920x1080&ratios=16x9");
        }

        var url = sb.ToString();
        Log.Info($"[wallhaven] 搜索: query={query} highRes={highRes} want={want}");

        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("wallhaven 限速（429），本分钟已超过 45 次");
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"wallhaven 返回 {(int)resp.StatusCode}");

        var json = await resp.Content.ReadAsStringAsync(ct);
        var items = Parse(json, want);
        Log.Info($"[wallhaven] 解析到 {items.Count} 条候选");
        return items;
    }

    /// <summary>
    /// 试搜：只看这个标签有没有图，不下载。
    /// 返回 null 表示连不上（网络/代理问题），0 表示搜到了但一张都没有，&gt;0 表示可用。
    /// </summary>
    public async Task<int?> ProbeTotalAsync(HttpClient http, string query, CancellationToken ct)
    {
        try
        {
            var url = $"{BaseUrl}?q={Uri.EscapeDataString(query)}&categories=100&purity=100&per_page=1";
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Log.Warn("[试搜] wallhaven 限速 429");
                return null;
            }
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warn($"[试搜] wallhaven 返回 {(int)resp.StatusCode}");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("total", out var total) &&
                total.TryGetInt32(out var n))
            {
                Log.Info($"[试搜] {query} → total={n}");
                return n;
            }
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"[试搜] 异常: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    internal static List<SourceItem> Parse(string json, int want)
    {
        var list = new List<SourceItem>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var el in data.EnumerateArray())
        {
            var id = Str(el, "id");
            var path = Str(el, "path");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(path)) continue;

            var w = Int(el, "dimension_x");
            var h = Int(el, "dimension_y");
            if (w <= 0 || h <= 0) continue;

            list.Add(new SourceItem(
                Id: "wallhaven:" + id,
                Source: "wallhaven",
                RemoteUrls: new[] { path },
                Width: w,
                Height: h,
                Bytes: Long(el, "file_size"),
                PageUrl: Str(el, "url"),
                Author: null));

            if (list.Count >= want) break;
        }
        return list;
    }

    private static string RandomSeed()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        lock (Rng)
        {
            var buf = new char[6];
            for (var i = 0; i < buf.Length; i++) buf[i] = chars[Rng.Next(chars.Length)];
            return new string(buf);
        }
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static long Long(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0;
}
