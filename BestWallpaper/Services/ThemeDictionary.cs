namespace BestWallpaper.Services;

/// <summary>
/// 中文 → wallhaven 标签的内置词典。只在"主题管理里输入中文但词库里没有"时用来生成标签。
/// 右侧一律是 wallhaven 上真能搜到的单词/短语；查不到才走在线翻译。
/// </summary>
public static class ThemeDictionary
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // 自然
        ["山"] = "mountains", ["山脉"] = "mountains", ["雪山"] = "mountain", ["山谷"] = "valley",
        ["海"] = "ocean", ["大海"] = "ocean", ["海洋"] = "ocean", ["海浪"] = "waves", ["海滩"] = "beach",
        ["森林"] = "forest", ["树林"] = "forest", ["树"] = "tree", ["草地"] = "grass",
        ["星空"] = "starry sky", ["星星"] = "stars", ["银河"] = "milky way", ["夜空"] = "night sky",
        ["云"] = "clouds", ["云海"] = "clouds", ["天空"] = "sky", ["日出"] = "sunrise", ["日落"] = "sunset",
        ["雪"] = "snow", ["雨"] = "rain", ["雾"] = "fog", ["湖"] = "lake", ["河"] = "river",
        ["瀑布"] = "waterfall", ["沙漠"] = "desert", ["岛"] = "island", ["花"] = "flower",
        ["花海"] = "flower field", ["樱花"] = "cherry blossom", ["枫叶"] = "autumn leaves", ["秋天"] = "autumn",
        ["春天"] = "spring", ["冬天"] = "winter", ["极光"] = "aurora", ["彩虹"] = "rainbow",

        // 动物
        ["猫"] = "cat", ["猫咪"] = "cat", ["小猫"] = "cat", ["狗"] = "dog", ["小狗"] = "dog",
        ["柴犬"] = "shiba inu", ["兔"] = "rabbit", ["兔子"] = "rabbit", ["狐狸"] = "fox",
        ["熊猫"] = "panda", ["老虎"] = "tiger", ["狮子"] = "lion", ["狼"] = "wolf", ["马"] = "horse",
        ["鸟"] = "bird", ["猫头鹰"] = "owl", ["鲸"] = "whale", ["海豚"] = "dolphin", ["鱼"] = "fish",
        ["鲨鱼"] = "shark", ["蝴蝶"] = "butterfly", ["鹿"] = "deer", ["熊"] = "bear", ["企鹅"] = "penguin",
        ["水母"] = "jellyfish", ["小动物"] = "animal", ["宠物"] = "pet",

        // 游戏 / 动漫
        ["游戏"] = "video game", ["游戏原画"] = "game art", ["像素风"] = "pixel art", ["像素"] = "pixel art",
        ["奇幻"] = "fantasy", ["魔幻"] = "fantasy", ["机甲"] = "mecha", ["机器人"] = "robot",
        ["动漫"] = "anime", ["二次元"] = "anime", ["插画"] = "illustration", ["角色"] = "character",
        ["黑暗风"] = "dark", ["幻想"] = "fantasy",

        // 极简 / 抽象
        ["极简"] = "minimalism", ["抽象"] = "abstract", ["几何"] = "geometry", ["渐变"] = "gradient",
        ["低多边形"] = "low poly", ["线条"] = "lines", ["纯色"] = "solid color", ["纹理"] = "texture",
        ["水墨"] = "ink painting", ["涂鸦"] = "graffiti",

        // 城市 / 赛博
        ["城市"] = "cityscape", ["城市夜景"] = "night city", ["赛博朋克"] = "cyberpunk", ["霓虹"] = "neon",
        ["夜景"] = "night", ["街道"] = "street", ["建筑"] = "architecture", ["桥"] = "bridge",
        ["东京"] = "tokyo", ["纽约"] = "new york", ["建筑群"] = "city", ["废墟"] = "ruins",

        // 太空 / 科幻
        ["太空"] = "space", ["星云"] = "nebula", ["科幻"] = "sci fi", ["星球"] = "planet",
        ["宇宙"] = "universe", ["银河系"] = "galaxy", ["火箭"] = "rocket", ["外星"] = "alien",
        ["黑洞"] = "black hole", ["宇航员"] = "astronaut",

        // 机械 / 载具
        ["汽车"] = "car", ["跑车"] = "supercar", ["摩托"] = "motorcycle", ["摩托车"] = "motorcycle",
        ["机械"] = "mechanical", ["飞机"] = "aircraft", ["战斗机"] = "fighter jet", ["坦克"] = "tank",
        ["火车"] = "train", ["船"] = "ship", ["单车"] = "bicycle", ["引擎"] = "engine",

        // 风格 / 名物
        ["星战"] = "star wars", ["星际迷航"] = "star trek", ["电影"] = "movie", ["复古"] = "retro",
        ["蒸汽波"] = "vaporwave", ["蒸汽朋克"] = "steampunk", ["暗黑"] = "dark", ["唯美"] = "beautiful",
        ["治愈"] = "cozy", ["日系"] = "japanese", ["国风"] = "chinese style", ["北欧"] = "nordic",
        ["哥特"] = "gothic", ["简约风"] = "minimal", ["科技"] = "technology", ["抽象艺术"] = "abstract art",
        ["油画"] = "painting", ["摄影"] = "photography", ["微距"] = "macro", ["极光夜景"] = "aurora night",
    };

    public static bool TryLookup(string chinese, out string tag)
    {
        tag = "";
        var key = Normalize(chinese);
        if (key.Length == 0) return false;
        if (Map.TryGetValue(key, out var hit))
        {
            tag = hit;
            return true;
        }
        return false;
    }

    public static int Count => Map.Count;

    private static string Normalize(string s)
        => (s ?? "").Trim().Replace(" ", "").Replace("　", "");
}
