using System.Net;
using BestWallpaper.Models;

namespace BestWallpaper.Services;

public static class HttpFactory
{
    public const string UserAgent = "BestWallpaper/1.0 (personal desktop wallpaper tool)";

    /// <summary>
    /// 系统模式交给 .NET 默认代理（会读 HTTP(S)_PROXY 与 WinINET 设置，本机的 Clash 就靠这个），
    /// 直连模式关掉代理，手动模式指定 host:port。
    /// </summary>
    public static HttpClient Create(AppConfig cfg, TimeSpan? timeout = null)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };

        switch (cfg.ProxyMode)
        {
            case ProxyMode.Direct:
                handler.UseProxy = false;
                break;
            case ProxyMode.Manual:
                handler.UseProxy = true;
                handler.Proxy = new WebProxy(NormalizeProxy(cfg.ProxyAddress));
                break;
            default:
                // 交给 HttpClient.DefaultProxy（系统设置）
                break;
        }

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(90),
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        return client;
    }

    private static string NormalizeProxy(string address)
    {
        var a = (address ?? "").Trim();
        if (a.Length == 0) return "http://127.0.0.1:7892";
        if (!a.Contains("://", StringComparison.Ordinal)) a = "http://" + a;
        return a;
    }
}
