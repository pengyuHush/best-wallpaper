using System.Text.Json;

namespace BestWallpaper.Services;

/// <summary>
/// Bing 每日图：免 key、直连可达（本机约 100ms），但不能按主题搜索——
/// 只作为第二层兜底：主源全挂时用最近 8 天的每日图凑池子。
/// </summary>
public sealed class BingSource : IImageSource
{
    public string Name => "bing";

    private const string FeedUrl = "https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=8&mkt=zh-CN";

    public async Task<IReadOnlyList<SourceItem>> SearchAsync(HttpClient http, string query, int want, bool highRes, CancellationToken ct)
    {
        Log.Info("[bing] 取最近 8 天每日图作为兜底素材");

        using var resp = await http.GetAsync(FeedUrl, HttpCompletionOption.ResponseContentRead, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"bing 返回 {(int)resp.StatusCode}");

        var json = await resp.Content.ReadAsStringAsync(ct);
        var items = Parse(json, want);
        Log.Info($"[bing] 解析到 {items.Count} 条候选");
        return items;
    }

    internal static List<SourceItem> Parse(string json, int want)
    {
        var list = new List<SourceItem>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var el in images.EnumerateArray())
        {
            var urlBase = Str(el, "urlbase");
            var startDate = Str(el, "startdate");
            if (string.IsNullOrEmpty(urlBase) || string.IsNullOrEmpty(startDate)) continue;

            var uhd = "https://www.bing.com" + urlBase + "_UHD.jpg";
            var fhd = "https://www.bing.com" + urlBase + "_1920x1080.jpg";

            list.Add(new SourceItem(
                Id: "bing:" + startDate,
                Source: "bing",
                RemoteUrls: new[] { uhd, fhd },
                Width: 3840,
                Height: 2160,
                Bytes: 0,
                PageUrl: Str(el, "copyrightlink"),
                Author: Str(el, "copyright")));

            if (list.Count >= want) break;
        }
        return list;
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
