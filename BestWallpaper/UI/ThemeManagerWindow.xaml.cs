using System.Threading;
using System.Windows;
using System.Windows.Controls;
using BestWallpaper.Models;
using BestWallpaper.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace BestWallpaper.UI;

/// <summary>
/// 主题管理：分组列表 + 搜索 + 行内编辑 + 启用/停用 + 恢复默认 + 删除 + 设为今日/固定。
/// 列表用代码构建（40 行左右的规模，比 DataTemplate + MVVM 更直白）。
/// </summary>
public partial class ThemeManagerWindow : Window
{
    private readonly Engine _engine;

    public ThemeManagerWindow(Engine engine)
    {
        _engine = engine;
        InitializeComponent();
        BuildList();
    }

    private void Status(string text) => StatusText.Text = text;

    private void BuildList()
    {
        ListPanel.Children.Clear();

        var all = ThemeCatalog.All(_engine.Config);
        var filter = (SearchBox.Text ?? "").Trim();
        var shown = all.Where(w => filter.Length == 0
                                   || w.Display.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                   || w.Query.Contains(filter, StringComparison.OrdinalIgnoreCase))
                       .ToList();

        SummaryText.Text = $"共 {all.Count} 个主题，当前显示 {shown.Count} 个" +
                           (_engine.IsTodayThemeActive ? $"　·　今日主题：{_engine.TodayThemeLabel}" : "");

        if (shown.Count == 0)
        {
            ListPanel.Children.Add(new TextBlock { Text = "没有匹配的主题", Foreground = (Brush)FindResource("MutedBrush") });
            return;
        }

        foreach (var group in shown.GroupBy(w => string.IsNullOrEmpty(w.Direction) ? ThemeCatalog.CustomGroup : w.Direction))
        {
            ListPanel.Children.Add(new TextBlock
            {
                Text = $"{group.Key}（{group.Count()}）",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 4),
            });

            foreach (var word in group) ListPanel.Children.Add(BuildRow(word));
        }
    }

    private FrameworkElement BuildRow(ThemeWord word)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var enable = new CheckBox
        {
            IsChecked = word.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "是否参与随机抽取",
        };
        enable.Click += (_, _) =>
        {
            _engine.SetThemeEnabled(word, enable.IsChecked == true);
            Status($"{(enable.IsChecked == true ? "已启用" : "已停用")}：{word.Display}");
            BuildList();
        };
        Grid.SetColumn(enable, 0);
        row.Children.Add(enable);

        var displayBox = new TextBox { Text = word.Display, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(displayBox, 1);
        row.Children.Add(displayBox);

        var queryBox = new TextBox { Text = word.Query, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(queryBox, 2);
        row.Children.Add(queryBox);

        var status = new TextBlock
        {
            Text = Describe(word),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(status, 3);
        row.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(buttons, 4);

        var save = MakeButton("保存");
        save.Click += async (_, _) => await SaveRowAsync(word, displayBox, queryBox, save);
        buttons.Children.Add(save);

        var today = MakeButton("设为今日");
        today.ToolTip = "只影响今天，并立刻重抓十张";
        today.Click += async (_, _) => await SetTodayAsync(word);
        buttons.Children.Add(today);

        var fixedBtn = MakeButton("设为固定");
        fixedBtn.Click += (_, _) =>
        {
            _engine.SetFixedTheme(word);
            Status($"已设为固定主题：{word.Display}（模式已切到「固定主题」）");
            BuildList();
        };
        buttons.Children.Add(fixedBtn);

        if (word.BuiltIn && word.Modified)
        {
            var restore = MakeButton("恢复默认");
            restore.Click += (_, _) =>
            {
                _engine.RestoreBuiltIn(word);
                Status($"已恢复默认：{word.OriginalQuery}");
                BuildList();
            };
            buttons.Children.Add(restore);
        }

        if (!word.BuiltIn)
        {
            var del = MakeButton("删除");
            del.Foreground = Brushes.Firebrick;
            del.Click += (_, _) =>
            {
                var isUsing = string.Equals(_engine.Config.TodayThemeQuery, word.Query, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(_engine.Config.FixedThemeQuery, word.Query, StringComparison.OrdinalIgnoreCase);
                var extra = isUsing ? "\n\n它正在被使用：删除后会自动退回「每日随机」（已下载的壁纸不受影响）。" : "";
                if (MessageBox.Show(this, $"删除主题「{word.Display}」（{word.Query}）？{extra}", "确认删除",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

                _engine.DeleteTheme(word);
                Status($"已删除：{word.Display}");
                BuildList();
            };
            buttons.Children.Add(del);
        }

        row.Children.Add(buttons);
        return row;
    }

    private static string Describe(ThemeWord word)
    {
        var parts = new List<string> { word.BuiltIn ? "内置" : "自定义" };
        if (word.Modified) parts.Add("已修改");
        if (!word.Verified) parts.Add("未验证");
        if (!word.Enabled) parts.Add("已停用");
        return string.Join(" · ", parts);
    }

    private Button MakeButton(string text)
        => new()
        {
            Content = text,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12,
        };

    private async Task SaveRowAsync(ThemeWord word, TextBox displayBox, TextBox queryBox, Button save)
    {
        var display = (displayBox.Text ?? "").Trim();
        var query = (queryBox.Text ?? "").Trim();
        if (display.Length == 0 || query.Length == 0)
        {
            Status("中文名和英文标签都不能为空");
            return;
        }
        if (display == word.Display && query == word.Query)
        {
            Status("没有改动");
            return;
        }

        var duplicate = ThemeCatalog.All(_engine.Config).FirstOrDefault(w =>
            !ReferenceEquals(w, word) && string.Equals(w.Query, query, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            Status($"标签「{query}」已被「{duplicate.Display}」占用");
            return;
        }

        save.IsEnabled = false;
        try
        {
            var tagChanged = !string.Equals(query, word.Query, StringComparison.OrdinalIgnoreCase);
            var verified = word.Verified;
            if (tagChanged)
            {
                Status($"正在试搜 {query} …");
                var total = await _engine.ProbeThemeAsync(query, CancellationToken.None);
                if (total is null)
                {
                    verified = false;
                    Status("无法联网验证，已按「未验证」保存；联网后请回来复查");
                }
                else if (total == 0)
                {
                    if (MessageBox.Show(this, $"标签「{query}」在 wallhaven 上搜不到图，仍要保存吗？", "试搜无结果",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    {
                        Status("已取消保存");
                        return;
                    }
                    verified = false;
                }
                else
                {
                    verified = true;
                    Status($"已确认：{query} 约有 {total} 张图");
                }
            }

            _engine.UpdateTheme(word, display, query, verified);
            BuildList();
        }
        finally
        {
            save.IsEnabled = true;
        }
    }

    private async Task SetTodayAsync(ThemeWord word)
    {
        Status($"正在把「{word.Display}」设为今日主题并重抓…");
        await _engine.ApplyTodayThemeAsync(word.Display, word.Query);
        Status($"已应用今日主题：{word.Display}（{word.Query}）");
        BuildList();
    }

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        var name = (NewDisplay.Text ?? "").Trim();
        if (name.Length == 0)
        {
            AddStatus.Text = "先填中文名，再点翻译";
            return;
        }
        var tag = await TryTranslateAsync(name);
        if (tag.Length > 0) NewQuery.Text = tag;
    }

    private async void NewQuery_LostFocus(object sender, RoutedEventArgs e)
    {
        var name = (NewDisplay.Text ?? "").Trim();
        var query = (NewQuery.Text ?? "").Trim();
        if (name.Length == 0 || query.Length > 0) return;
        var tag = await TryTranslateAsync(name);
        if (tag.Length > 0 && (NewQuery.Text ?? "").Trim().Length == 0) NewQuery.Text = tag;
    }

    private async Task<string> TryTranslateAsync(string chinese)
    {
        AddStatus.Text = "正在翻译…";
        var result = await _engine.TranslateAsync(chinese, CancellationToken.None);
        if (result.Tag is null)
        {
            AddStatus.Text = $"没翻出可用的标签（{result.Source}），请手填英文标签";
            return "";
        }

        AddStatus.Text = result.Source == "词典"
            ? $"词典命中：{result.Tag}"
            : $"在线翻译：{result.Tag}（原始「{result.Raw}」），请确认后再添加";
        return result.Tag;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var display = (NewDisplay.Text ?? "").Trim();
        var query = (NewQuery.Text ?? "").Trim();

        if (display.Length == 0)
        {
            AddStatus.Text = "中文名不能为空";
            return;
        }

        var existing = ThemeCatalog.Find(_engine.Config, display);
        if (existing is not null)
        {
            AddStatus.Text = $"「{existing.Display}」已存在（标签 {existing.Query}），可直接在下面列表里点「设为今日」";
            return;
        }

        if (query.Length == 0)
        {
            query = await TryTranslateAsync(display);
            if (query.Length == 0) return;
            NewQuery.Text = query;
        }

        AddStatus.Text = $"正在试搜 {query} …";
        var total = await _engine.ProbeThemeAsync(query, CancellationToken.None);
        var verified = false;
        if (total is null)
        {
            AddStatus.Text = "网络不可用，将按「未验证」保存";
        }
        else if (total == 0)
        {
            if (MessageBox.Show(this, $"标签「{query}」在 wallhaven 上搜不到图，仍要保存吗？", "试搜无结果",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                AddStatus.Text = "已取消添加";
                return;
            }
        }
        else
        {
            verified = true;
            AddStatus.Text = $"已确认：{query} 约有 {total} 张图";
        }

        var word = _engine.AddCustomTheme(display, query, verified);
        if (word is null)
        {
            AddStatus.Text = "添加失败：标签已被占用或输入无效";
            return;
        }

        AddStatus.Text = $"已添加：{display}（{query}）{(verified ? "，已验证" : "，未验证")}";
        NewDisplay.Text = "";
        NewQuery.Text = "";
        BuildList();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => BuildList();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
