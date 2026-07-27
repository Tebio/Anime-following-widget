using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AnimeWidget;

public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public class EntryViewModel
{
    public required Entry Model { get; init; }
    public required string BaseUrl { get; init; }
    /// <summary>今日条目且播出时间已过 → 置灰。</summary>
    public bool IsPast { get; init; }

    public string Title => Model.Title;
    public bool IsNew => Model.IsNew;
    public bool IsEnd => Model.IsEnd;
    public string TimeText => Model.Time ?? "";
    public string LabelText => Model.Label;
    public double RowOpacity => IsPast ? 0.45 : 1.0;

    public string Url(ClickTarget target) =>
        target == ClickTarget.Detail ? Model.DetailUrl(BaseUrl) : Model.SearchUrl(BaseUrl);
}

public class AppViewModel : ObservableBase
{
    private WeekSchedule? _sched;

    public ObservableCollection<EntryViewModel> Entries { get; } = new();

    private int _selectedDay = ScheduleService.TodayIndex();
    public int SelectedDay { get => _selectedDay; set { if (Set(ref _selectedDay, value)) RefreshEntries(); } }

    public int TodayIndex => ScheduleService.TodayIndex();

    private string _subtitle = "";
    public string Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }

    private string _sourceText = "";
    public string SourceText { get => _sourceText; set => Set(ref _sourceText, value); }

    private string? _errorText;
    public string? ErrorText { get => _errorText; set => Set(ref _errorText, value); }

    public bool HasError => ErrorText != null;

    private SolidColorBrush _accentBrush = new(Color.FromRgb(45, 212, 191));
    public SolidColorBrush AccentBrush { get => _accentBrush; set => Set(ref _accentBrush, value); }

    public event Action? DayChanged;

    public void ApplySchedule(WeekSchedule sched)
    {
        _sched = sched;
        var today = sched.Days.ElementAtOrDefault(TodayIndex);
        Subtitle = $"{DateTime.Now:M月d日} {ScheduleService.WeekdayNames[TodayIndex]}"
            + (today != null ? $" · 今日 {today.Entries.Count} 部更新" : "");
        SourceText = $"更新于 {sched.FetchedAt} · {new Uri(sched.Base).Host}";
        // 陈旧缓存提示：抓取时间距今超 36 小时（多为上周缓存）
        if (DateTime.TryParseExact(sched.FetchedAt, "yyyy-MM-dd HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var fetched)
            && DateTime.Now - fetched > TimeSpan.FromHours(36))
            SourceText += "（缓存较旧）";
        ErrorText = null;
        RefreshEntries();
    }

    public void ApplyError(string error)
    {
        ErrorText = error;
        Raise(nameof(HasError));
    }

    public void ClearError()
    {
        ErrorText = null;
        Raise(nameof(HasError));
    }

    public void RefreshEntries()
    {
        Entries.Clear();
        var day = _sched?.Days.ElementAtOrDefault(SelectedDay);
        if (day == null) return;
        var isToday = SelectedDay == TodayIndex;
        var now = DateTime.Now.TimeOfDay;
        foreach (var e in day.Entries)
        {
            var isPast = isToday
                && TimeSpan.TryParseExact(e.Time, "hh\\:mm", null, out var t)
                && t < now;
            Entries.Add(new EntryViewModel { Model = e, BaseUrl = _sched!.Base, IsPast = isPast });
        }
        DayChanged?.Invoke();
    }

    public int SelectedDayCount => _sched?.Days.ElementAtOrDefault(SelectedDay)?.Entries.Count ?? 0;
}
