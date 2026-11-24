using PressViewer.Services;
using Brushes = System.Windows.Media.Brushes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PressViewer.Services.Interfaces;
// 项目同时启用 WinForms，消除与 System.Drawing 的歧义。
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace PressViewer.ViewModels;

/// <summary>
/// 热力图独立页：上半部分是键盘，下面是鼠标点击计数。
/// 键盘与鼠标都使用粉色刻度，但各自按自己的最大值做相对映射，互不影响。
/// 时间范围：总计 / 今日 / 昨天 / 前天 / 近七天 / 近三十天 / 自定义日期范围。
/// </summary>
public partial class HeatmapViewModel : ViewModelBase
{
    public const int ModeAll = 0;
    public const int ModeToday = 1;
    public const int ModeYesterday = 2;
    public const int ModeDayBeforeYesterday = 3;
    public const int ModeLast7 = 4;
    public const int ModeLast30 = 5;
    public const int ModeCustom = 6;

    private readonly KeyCounterViewModel _keyVm;
    private readonly ClickCounterViewModel _clickVm;
    private readonly IKeyDatabaseService _keyDb;
    private readonly IClickDatabaseService _clickDb;

    private readonly ObservableCollection<KeyCountItem> _rangeKeys = new();
    private (DateTime from, DateTime to) _loadedRange;
    private bool _rangeLoaded;

    /// <summary>0 总计 / 1 今日 / 2 昨天 / 3 前天 / 4 近七天 / 5 近三十天 / 6 自定义。</summary>
    [ObservableProperty]
    private int _modeIndex;

    [ObservableProperty]
    private string _rangeLabel = "全部时间";

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private bool _isCustomMode;

    /// <summary>下拉框里的选项，顺序与 ModeIndex 一一对应。</summary>
    public IReadOnlyList<string> RangeOptions { get; } =
        new[] { "总计", "今日", "昨天", "前天", "近七天", "近三十天", "自定义…" };

    public ObservableCollection<MouseCard> MouseCards { get; } = new();

    /// <summary>鼠标图形的数据源：Left/Middle/Right 三项，跟随当前范围。</summary>
    public ObservableCollection<KeyCountItem> MouseShapeItems { get; } = new();

    public GamepadCounterViewModel Gamepad { get; }

    /// <summary>鼠标图形下方的计数图例（只列非零项）。</summary>
    public string MouseLegendText
    {
        get
        {
            var parts = new List<string>();
            foreach (var (id, name) in new[] { ("Left", "左键"), ("Middle", "滚轮"), ("Right", "右键") })
            {
                int c = MouseShapeItems.FirstOrDefault(x => x.Key == id)?.Count ?? 0;
                if (c > 0)
                    parts.Add($"{name} {NumberFormatService.Format(c)}");
            }
            return parts.Count == 0 ? "暂无点击" : string.Join("   ·   ", parts);
        }
    }

    /// <summary>鼠标图形卡片右上角的总计文字。</summary>
    public string MouseTotalText
    {
        get
        {
            int sum = MouseShapeItems.Sum(x => x.Count);
            return $"总计 {NumberFormatService.Format(sum)} 次";
        }
    }

    /// <summary>当前范围对应的按键计数；总计/今天/昨天/前天直接引用记录页的实时集合。</summary>
    public IEnumerable<KeyCountItem> Keys
    {
        get
        {
            switch (ModeIndex)
            {
                case ModeAll:
                    return _keyVm.KeyCounts;
                case ModeToday:
                    return _keyVm.TodayKeyCounts;
                case ModeYesterday:
                    return _keyVm.YesterdayKeyCounts;
                case ModeDayBeforeYesterday:
                    return _keyVm.DayBeforeYesterdayKeyCounts;
                default:
                    return LoadRangeKeys();
            }
        }
    }

    public HeatmapViewModel(
        KeyCounterViewModel keyVm,
        ClickCounterViewModel clickVm,
        IKeyDatabaseService keyDb,
        IClickDatabaseService clickDb,
        GamepadCounterViewModel gamepad
    )
    {
        _keyVm = keyVm;
        _clickVm = clickVm;
        _keyDb = keyDb;
        _clickDb = clickDb;
        Gamepad = gamepad;

        MouseCards.Add(new MouseCard("左键"));
        MouseCards.Add(new MouseCard("中键"));
        MouseCards.Add(new MouseCard("右键"));

        _clickVm.PropertyChanged += OnClickVmPropertyChanged;
        ThemeService.Changed += Refresh;
        Refresh();
    }

    private void OnClickVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            ModeIndex is ModeAll or ModeToday
            && e.PropertyName
                is nameof(ClickCounterViewModel.LeftClickCount)
                    or nameof(ClickCounterViewModel.MiddleClickCount)
                    or nameof(ClickCounterViewModel.RightClickCount)
                    or nameof(ClickCounterViewModel.LeftClickToday)
                    or nameof(ClickCounterViewModel.MiddleClickToday)
                    or nameof(ClickCounterViewModel.RightClickToday)
        )
        {
            UpdateMouse();
        }
    }

    partial void OnModeIndexChanged(int value)
    {
        IsCustomMode = value == ModeCustom;
        if (value == ModeCustom)
        {
            StartDate ??= DateTime.Today.AddDays(-6);
            EndDate ??= DateTime.Today;
        }

        _rangeLoaded = false;
        Refresh();
    }

    /// <summary>把自定义起止日期应用到当前范围（起止颠倒时自动交换）。</summary>
    public void ApplyCustomRange()
    {
        if (StartDate is DateTime s && EndDate is DateTime e && e.Date < s.Date)
        {
            (StartDate, EndDate) = (e.Date, s.Date);
        }

        _rangeLoaded = false;
        ModeIndex = ModeCustom;
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Keys));
        RangeLabel = ModeIndex switch
        {
            ModeAll => "全部时间",
            ModeToday => "今天",
            ModeYesterday => "昨天",
            ModeDayBeforeYesterday => "前天",
            ModeLast7 => "近七天",
            ModeLast30 => "近三十天",
            _ => StartDate is DateTime s && EndDate is DateTime e
                ? $"{s:yyyy-MM-dd} ~ {e:yyyy-MM-dd}"
                : "自定义范围",
        };

        UpdateMouse();
    }

    private (DateTime from, DateTime to)? CurrentRange()
    {
        var today = DateTime.Today;
        return ModeIndex switch
        {
            ModeLast7 => (today.AddDays(-6), today.AddDays(1)),
            ModeLast30 => (today.AddDays(-29), today.AddDays(1)),
            ModeCustom when StartDate is DateTime s && EndDate is DateTime e && e.Date >= s.Date =>
                (s.Date, e.Date.AddDays(1)),
            _ => null,
        };
    }

    private IEnumerable<KeyCountItem> LoadRangeKeys()
    {
        var range = CurrentRange();
        if (range is not (DateTime from, DateTime to))
            return _rangeKeys;

        if (_rangeLoaded && _loadedRange == (from, to))
            return _rangeKeys;

        var counts = _keyDb.GetKeyCounts(from, to);
        _rangeKeys.Clear();
        foreach (var kv in counts.OrderByDescending(x => x.Value))
            _rangeKeys.Add(new KeyCountItem { Key = kv.Key, Count = kv.Value });
        _loadedRange = (from, to);
        _rangeLoaded = true;
        return _rangeKeys;
    }

    private void UpdateMouse()
    {
        switch (ModeIndex)
        {
            case ModeAll:
                SetMouseCards(
                    _clickVm.LeftClickCount,
                    _clickVm.MiddleClickCount,
                    _clickVm.RightClickCount
                );
                return;
            case ModeToday:
                SetMouseCards(
                    _clickVm.LeftClickToday,
                    _clickVm.MiddleClickToday,
                    _clickVm.RightClickToday
                );
                return;
        }

        var range = CurrentRange();
        if (range is not (DateTime from, DateTime to))
        {
            SetMouseCards(0, 0, 0);
            return;
        }

        var counts = _clickDb.GetClickCounts(from, to);
        SetMouseCards(counts["Left"], counts["Middle"], counts["Right"]);
    }

    private void SetMouseCards(int left, int middle, int right)
    {
        MouseCards[0].Count = left;
        MouseCards[1].Count = middle;
        MouseCards[2].Count = right;

        int max = Math.Max(1, Math.Max(left, Math.Max(middle, right)));
        int min = new[] { left, middle, right }.Where(v => v > 0).DefaultIfEmpty(1).Min();
        ApplyHeat(MouseCards[0], left, max, min);
        ApplyHeat(MouseCards[1], middle, max, min);
        ApplyHeat(MouseCards[2], right, max, min);

        SetShape(MouseShapeItems, "Left", left);
        SetShape(MouseShapeItems, "Middle", middle);
        SetShape(MouseShapeItems, "Right", right);
        OnPropertyChanged(nameof(MouseTotalText));
        OnPropertyChanged(nameof(MouseLegendText));
    }

    private static void SetShape(ObservableCollection<KeyCountItem> list, string key, int count)
    {
        var item = list.FirstOrDefault(x => x.Key == key);
        if (item == null)
            list.Add(new KeyCountItem { Key = key, Count = count });
        else
            item.Count = count;
    }

    private static void ApplyHeat(MouseCard card, int count, int max, int min)
    {
        // 0 次不着色（与键盘一致地回到卡片底色）；有次数才按主题色渐变
        if (count <= 0)
        {
            card.Background = new SolidColorBrush(ThemeService.HeatIdle);
            card.Foreground = new SolidColorBrush(ThemeService.MutedInk);
            return;
        }

        var (bg, fg) = ThemeService.Heat(count, max, min);
        card.Background = new SolidColorBrush(bg);
        card.Foreground = new SolidColorBrush(fg);
    }
}

/// <summary>鼠标热图里的一张按键卡片（左键/中键/右键）。</summary>
public partial class MouseCard : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private Brush _background = Brushes.Transparent;

    [ObservableProperty]
    private Brush _foreground = Brushes.Transparent;

    public MouseCard(string name) => Name = name;
}
