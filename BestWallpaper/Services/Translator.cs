using System.Text.Json;
using System.Text.RegularExpressions;

namespace BestWallpaper.Services;

/// <summary>
/// 把用户输入的中文变成 wallhaven 认的英文标签。
/// 顺序：内置词典 → 在线翻译（MyMemory，免费无 key）→ 失败就留空让人手填。
/// 在线结果会清洗（小写、去冠词、去标点、最多 3 个词），因为机器翻译常给出 "The pussy cats" 这类没法搜的东西。
/// </summary>
public static class Translator
{
    public sealed record Result(string? Tag, string Source, string? Raw);

    private const string Endpoint = "https://api.mymemory.translated.net/get";

    public static Result FromDictionary(string chinese)
        => ThemeDictionary.TryLookup(chinese, out var tag)
            ? new Result(tag, "词典", null)
            : new Result(null, "未命中", null);

    public static async Task<Result> TranslateAsync(string chinese, HttpClient http, CancellationToken ct)
    {
        var dict = FromDictionary(chinese);
        if (dict.Tag is not null) return dict;

        var text = (chinese ?? "").Trim();
        if (text.Length == 0) return new Result(null, "空输入", null);

        try
        {
            var url = $"{Endpoint}?q={Uri.EscapeDataString(text)}&langpair=zh-CN|en";
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warn($"[翻译] 在线翻译返回 {(int)resp.StatusCode}");
                return new Result(null, "在线失败", null);
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("responseData", out var data) ||
                !data.TryGetProperty("translatedText", out var translated))
            {
                return new Result(null, "在线失败", null);
            }

            var raw = translated.GetString() ?? "";
            var cleaned = Clean(raw);
            Log.Info($"[翻译] {text} → 原始「{raw}」→ 清洗后「{cleaned}」");
            return cleaned.Length == 0
                ? new Result(null, "在线结果不可用", raw)
                : new Result(cleaned, "在线翻译", raw);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"[翻译] 在线翻译异常: {ex.GetType().Name}: {ex.Message}");
            return new Result(null, "无法联网", null);
        }
    }

    /// <summary>把 "The pussy cats" 这类结果清洗成 wallhaven 能用的标签。</summary>
    internal static string Clean(string raw)
    {
        var s = (raw ?? "").ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s\-]", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @"^(the|a|an)\s+", "");
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 3) s = string.Join(' ', words.Take(3));
        return s.Trim();
    }
}
