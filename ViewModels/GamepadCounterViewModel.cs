using Color = System.Windows.Media.Color;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PressViewer.Models;
using PressViewer.Services.Interfaces;
using Brush = System.Windows.Media.Brush;
using WpfApplication = System.Windows.Application;

namespace PressViewer.ViewModels;

public partial class GamepadCounterViewModel : ViewModelBase
{
    private readonly IGamepadHookService _hookService;
    private readonly IGamepadDatabaseService _dbService;
    private readonly IConfigurationService _configService;
    private DateTime _currentDate = DateTime.Today;

    public ObservableCollection<KeyCountItem> GamepadCounts { get; } = new();

    public ObservableCollection<KeyCountItem> TodayGamepadCounts { get; } = new();

    /// <summary>历史日期查询结果（手柄记录页底部的「查看历史」）。</summary>
    public ObservableCollection<KeyCountItem> HistoryGamepadCounts { get; } = new();

    [ObservableProperty]
    private DateTime? _selectedDate = DateTime.Today;

    [ObservableProperty]
    private bool _isRecording;

    public int HistoryTotal => HistoryGamepadCounts.Sum(item => item.Count);

    public int GamepadTodayPresses => TodayGamepadCounts.Sum(item => item.Count);

    public int GamepadTotalPresses => GamepadCounts.Sum(item => item.Count);

    public int CoveredGamepadButtons => GamepadCounts.Count(item => item.Count > 0);

    public string GamepadRecordingStatus => IsRecording ? "记录中" : "已停止";

    public Brush GamepadRecordingColor =>
        IsRecording
            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
            : new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

    public GamepadCounterViewModel(
        IGamepadHookService hookService,
        IGamepadDatabaseService dbService,
        IConfigurationService configService
    )
    {
        _hookService = hookService;
        _dbService = dbService;
        _configService = configService;

        _hookService.GamepadPressed += OnGamepadPressed;

        LoadAllCounts();
        LoadCountsForDate(DateTime.Today);

        if (_configService.GetGamepadRecordingAutoStart())
            StartRecording();
    }

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(GamepadRecordingStatus));
        OnPropertyChanged(nameof(GamepadRecordingColor));
    }

    partial void OnSelectedDateChanged(DateTime? value)
    {
        if (value.HasValue)
            LoadCountsForDate(value.Value);
    }

    public void LoadCountsForDate(DateTime date)
    {
        var app = WpfApplication.Current;
        if (app == null)
            return;

        app.Dispatcher.Invoke(() =>
        {
            var counts = _dbService.GetPressCounts(date.Date, date.Date.AddDays(1));
            HistoryGamepadCounts.Clear();
            foreach (var kv in counts.OrderByDescending(x => x.Value))
                HistoryGamepadCounts.Add(new KeyCountItem { Key = kv.Key, Count = kv.Value });
            OnPropertyChanged(nameof(HistoryTotal));
        });
    }

    private void OnGamepadPressed(string button)
    {
        _dbService.SavePress(
            new GamepadPressRecord { Button = button, PressTime = DateTime.Now }
        );

        DateTime today = DateTime.Today;
        if (today > _currentDate)
        {
            _currentDate = today;
            LoadAllCounts();
            return;
        }

        WpfApplication.Current?.Dispatcher.InvokeAsync(() =>
        {
            UpdateCollection(GamepadCounts, button);
            UpdateCollection(TodayGamepadCounts, button);

            // 「查看历史」正停在今天时也要跟着涨，不需要手动刷新
            if (SelectedDate?.Date == today)
            {
                UpdateCollection(HistoryGamepadCounts, button);
                OnPropertyChanged(nameof(HistoryTotal));
            }

            OnPropertyChanged(nameof(GamepadTodayPresses));
            OnPropertyChanged(nameof(GamepadTotalPresses));
            OnPropertyChanged(nameof(CoveredGamepadButtons));
        });
    }

    private static void UpdateCollection(ObservableCollection<KeyCountItem> collection, string id)
    {
        var item = collection.FirstOrDefault(x => x.Key == id);
        if (item != null)
            item.Count++;
        else
            collection.Add(new KeyCountItem { Key = id, Count = 1 });
    }

    private void LoadAllCounts()
    {
        var app = WpfApplication.Current;
        if (app == null)
            return;

        app.Dispatcher.Invoke(() =>
        {
            var total = _dbService.GetPressCounts();
            GamepadCounts.Clear();
            foreach (var kv in total.OrderByDescending(x => x.Value))
                GamepadCounts.Add(new KeyCountItem { Key = kv.Key, Count = kv.Value });

            var today = _dbService.GetPressCounts(DateTime.Today, DateTime.Today.AddDays(1));
            TodayGamepadCounts.Clear();
            foreach (var kv in today.OrderByDescending(x => x.Value))
                TodayGamepadCounts.Add(new KeyCountItem { Key = kv.Key, Count = kv.Value });

            OnPropertyChanged(nameof(GamepadTodayPresses));
            OnPropertyChanged(nameof(GamepadTotalPresses));
            OnPropertyChanged(nameof(CoveredGamepadButtons));
        });
    }

    public int GetCount(string button) =>
        GamepadCounts.FirstOrDefault(x => x.Key == button)?.Count ?? 0;

    [RelayCommand]
    private void StartRecording()
    {
        _hookService.Start();
        IsRecording = true;
        _configService.SetGamepadRecordingAutoStart(true);
    }

    [RelayCommand]
    private void StopRecording()
    {
        _hookService.Stop();
        IsRecording = false;
        _configService.SetGamepadRecordingAutoStart(false);
    }

    [RelayCommand]
    private void RefreshData()
    {
        _currentDate = DateTime.Today;
        LoadAllCounts();
        LoadCountsForDate(SelectedDate ?? DateTime.Today);
    }
}
