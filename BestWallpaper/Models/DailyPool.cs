namespace BestWallpaper.Models;

public sealed class WallpaperItem
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string File { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long Bytes { get; set; }
    public string? PageUrl { get; set; }
    public string? Author { get; set; }
    public string? RemoteUrl { get; set; }
}

public sealed class DailyPool
{
    public string Date { get; set; } = "";
    public string ThemeDisplay { get; set; } = "";
    public string ThemeQuery { get; set; } = "";
    public string BuiltAt { get; set; } = "";
    public List<WallpaperItem> Items { get; set; } = new();

    public WallpaperItem? ById(string id) => Items.FirstOrDefault(i => i.Id == id);
}
