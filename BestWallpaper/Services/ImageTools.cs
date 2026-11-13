using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace BestWallpaper.Services;

/// <summary>
/// 图片后处理：裁掉源图自带的黑边（电影截图常见），再裁到屏幕比例。
/// 壁纸填充模式是 Fill，但只要源图把黑边"烤"进像素里，Fill 也救不了——只能裁。
/// </summary>
public static class ImageTools
{
    private const double BlackMean = 8.0;    // 整行/整列平均亮度低于它才可能是黑边
    private const double BlackMax = 32.0;    // 且最亮的采样点也不能超过它
    private const double MaxTrimRatio = 0.45; // 单边最多裁掉 45%，防误判把整图裁没

    public sealed record TrimResult(bool Modified, string Detail);

    /// <summary>取最大那块屏幕的物理像素尺寸（进程已声明 per-monitor DPI 感知）。</summary>
    public static (int W, int H) TargetScreenSize()
    {
        if (_screen is { } cached) return cached;
        try
        {
            var screens = WinForms.Screen.AllScreens;
            if (screens.Length == 0) { _screen = (1920, 1080); return _screen.Value; }
            var best = screens.OrderByDescending(s => (long)s.Bounds.Width * s.Bounds.Height).First();
            _screen = (best.Bounds.Width, best.Bounds.Height);
        }
        catch
        {
            _screen = (1920, 1080);
        }
        return _screen.Value;
    }

    private static (int, int)? _screen;

    /// <summary>
    /// 裁掉黑边并裁到 targetW:targetH。没有黑边且比例已合适时不做任何事（避免无意义的重编码）。
    /// </summary>
    public static TrimResult Process(string path, int targetW, int targetH)
    {
        try
        {
            if (!File.Exists(path)) return new TrimResult(false, "文件不存在");
            if (targetW <= 0 || targetH <= 0) return new TrimResult(false, "目标尺寸无效");

            var frame = BitmapFrame.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var source = frame.Format == PixelFormats.Bgra32
                ? (BitmapSource)frame
                : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

            var w = source.PixelWidth;
            var h = source.PixelHeight;
            var stride = w * 4;
            var pixels = new byte[(long)stride * h];
            source.CopyPixels(pixels, stride, 0);

            var top = 0;
            while (top < h && IsBlackRow(pixels, stride, w, top) && top < h * MaxTrimRatio) top++;
            var bottom = h - 1;
            while (bottom > 0 && IsBlackRow(pixels, stride, w, bottom) && (h - 1 - bottom) < h * MaxTrimRatio) bottom--;

            var left = 0;
            while (left < w && IsBlackColumn(pixels, stride, w, h, left) && left < w * MaxTrimRatio) left++;
            var right = w - 1;
            while (right > 0 && IsBlackColumn(pixels, stride, w, h, right) && (w - 1 - right) < w * MaxTrimRatio) right--;

            var contentW = right - left + 1;
            var contentH = bottom - top + 1;
            if (contentW < w * 0.3 || contentH < h * 0.3) return new TrimResult(false, "黑边占比过大，放弃裁剪");

            // 再裁到屏幕比例：先按内容框居中，再把长的一边削掉
            var targetRatio = (double)targetW / targetH;
            var cropX = left;
            var cropY = top;
            var cropW = contentW;
            var cropH = contentH;

            var ratio = (double)cropW / cropH;
            if (ratio > targetRatio)
            {
                cropW = (int)Math.Round(cropH * targetRatio);
                cropX = left + (contentW - cropW) / 2;
            }
            else if (ratio < targetRatio)
            {
                cropH = (int)Math.Round(cropW / targetRatio);
                cropY = top + (contentH - cropH) / 2;
            }

            cropW = Math.Min(cropW, w - cropX);
            cropH = Math.Min(cropH, h - cropY);

            var bars = top + (h - 1 - bottom) + left + (w - 1 - right);
            var needsCrop = bars > 2 || cropW != w || cropH != h;
            if (!needsCrop) return new TrimResult(false, "无黑边，比例已匹配");

            var cropped = new CroppedBitmap(source, new Int32Rect(cropX, cropY, cropW, cropH));
            var encoder = ChooseEncoder(path);
            encoder.Frames.Add(BitmapFrame.Create(cropped));

            var tmp = path + ".tmp";
            using (var fs = File.Create(tmp)) encoder.Save(fs);

            var oldLength = new FileInfo(path).Length;
            File.Move(tmp, path, overwrite: true);
            var newLength = new FileInfo(path).Length;

            var detail = $"裁边 top={top} bottom={h - 1 - bottom} left={left} right={w - 1 - right}；" +
                         $"{w}x{h} → {cropW}x{cropH}；{oldLength / 1024}KB → {newLength / 1024}KB";
            Log.Info($"[图片] {Path.GetFileName(path)} {detail}");
            return new TrimResult(true, detail);
        }
        catch (Exception ex)
        {
            Log.Warn($"[图片] 处理 {Path.GetFileName(path)} 失败: {ex.GetType().Name}: {ex.Message}");
            return new TrimResult(false, "处理失败: " + ex.Message);
        }
    }

    private static BitmapEncoder ChooseEncoder(string path)
        => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? new PngBitmapEncoder()
            : new JpegBitmapEncoder { QualityLevel = 92 };

    /// <summary>整行采样：亮度均值很低且最大值也不高，才算黑边。</summary>
    private static bool IsBlackRow(byte[] pixels, int stride, int width, int y)
    {
        var step = Math.Max(1, width / 160);
        var sum = 0.0;
        var max = 0.0;
        var n = 0;
        var baseIdx = y * stride;
        for (var x = 0; x < width; x += step)
        {
            var i = baseIdx + x * 4;
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var lum = 0.114 * b + 0.587 * g + 0.299 * r;
            sum += lum;
            if (lum > max) max = lum;
            n++;
        }
        return n > 0 && sum / n < BlackMean && max < BlackMax;
    }

    private static bool IsBlackColumn(byte[] pixels, int stride, int width, int height, int x)
    {
        var step = Math.Max(1, height / 160);
        var sum = 0.0;
        var max = 0.0;
        var n = 0;
        for (var y = 0; y < height; y += step)
        {
            var i = y * stride + x * 4;
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var lum = 0.114 * b + 0.587 * g + 0.299 * r;
            sum += lum;
            if (lum > max) max = lum;
            n++;
        }
        return n > 0 && sum / n < BlackMean && max < BlackMax;
    }
}
