using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusFence.Core;

namespace FocusFence.App;

public partial class ScheduleEditor : UserControl
{
    private AppState? state;
    private Func<bool> save = () => false;
    private Func<bool> canEdit = () => false;
    private Action<string> status = _ => { };
    private string timeZoneId = TimeZoneInfo.Local.Id;
    private FocusSchedule? Selected => SavedSchedules.SelectedItem as FocusSchedule;

    public ScheduleEditor() { InitializeComponent(); }

    public void Initialize(AppState appState, Func<bool> saveState, Func<bool> canEditState, Action<string> showStatus)
    {
        state = appState; save = saveState; canEdit = canEditState; status = showStatus;
        Refresh(); ResetEditor();
    }

    public void Refresh()
    {
        var id = Selected?.Id;
        var blocklistId = (ScheduleBlocklist.SelectedItem as Blocklist)?.Id;
        SavedSchedules.ItemsSource = state?.Schedules.ToList();
        SavedSchedules.SelectedItem = state?.Schedules.FirstOrDefault(s => s.Id == id);
        ScheduleBlocklist.ItemsSource = state?.Blocklists.ToList();
        ScheduleBlocklist.SelectedItem = state?.Blocklists.FirstOrDefault(b => b.Id == blocklistId) ?? state?.Blocklists.FirstOrDefault();
    }

    private void ResetEditor()
    {
        SavedSchedules.SelectedItem = null;
        ScheduleName.Text = "";
        StartTime.Text = Defaults.ScheduleStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        EndTime.Text = Defaults.ScheduleEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
        timeZoneId = TimeZoneInfo.Local.Id;
        ZoneLabel.Text = "Time zone: " + TimeZoneInfo.Local.DisplayName;
        Days.ItemsSource = CreateDays(null);
        ScheduleEnabled.IsChecked = false;
    }

    private static DayChoice[] CreateDays(FocusSchedule? schedule) => Enumerable.Range(0, 7)
        .Select(i => (DayOfWeek)((i + (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek) % 7))
        .Select(day => new DayChoice(day, schedule?.Days.Contains(day) ?? (day is not DayOfWeek.Saturday and not DayOfWeek.Sunday))).ToArray();

    private void NewSchedule(object sender, RoutedEventArgs e) { if (canEdit()) ResetEditor(); }

    private void SelectSchedule(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } schedule) return;
        ScheduleName.Text = schedule.Name;
        ScheduleBlocklist.SelectedItem = state?.Blocklists.FirstOrDefault(b => b.Id == schedule.BlocklistId);
        StartTime.Text = schedule.StartsAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        EndTime.Text = schedule.EndsAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        Days.ItemsSource = CreateDays(schedule);
        ScheduleEnabled.IsChecked = schedule.Enabled;
        timeZoneId = schedule.TimeZoneId;
        ZoneLabel.Text = "Time zone: " + TimeZoneInfo.FindSystemTimeZoneById(timeZoneId).DisplayName;
    }

    private void SaveSchedule(object sender, RoutedEventArgs e)
    {
        if (state is null || !canEdit()) return;
        if (ScheduleBlocklist.SelectedItem is not Blocklist blocklist) { status("Create and select a blocklist first."); return; }
        if (!TimeOnly.TryParseExact(StartTime.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(EndTime.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        { status("Enter start and end times in 24-hour HH:mm format, for example 09:00."); return; }
        var schedule = new FocusSchedule(Selected?.Id ?? Guid.NewGuid(), ScheduleName.Text.Trim(), blocklist.Id,
            Days.ItemsSource.Cast<DayChoice>().Where(d => d.Selected).Select(d => d.Day).ToList(), start, end, timeZoneId, ScheduleEnabled.IsChecked == true);
        if (SchedulePlanner.Validate(schedule, state.Blocklists) is { } error) { status(error); return; }
        if (schedule.Enabled && blocklist.Apps.Count == 0) { status("Add apps to the blocklist before enabling this schedule."); return; }
        if (schedule.Enabled && MessageBox.Show("This schedule will close apps automatically, including now if its hours are active. Unsaved work may be lost. Enable it?", "Enable schedule", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var previous = state.Schedules.ToList();
        state.Schedules = state.Schedules.Where(s => s.Id != schedule.Id).Append(schedule).ToList();
        if (!save()) { state.Schedules = previous; return; }
        Refresh(); SavedSchedules.SelectedItem = schedule;
        status("Schedule saved. FocusFence must remain running for schedules to block apps.");
    }

    private void DeleteSchedule(object sender, RoutedEventArgs e)
    {
        if (state is null || !canEdit() || Selected is not { } schedule) return;
        if (MessageBox.Show($"Delete '{schedule.Name}'?", "Delete schedule", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var previous = state.Schedules.ToList();
        state.Schedules = state.Schedules.Where(s => s.Id != schedule.Id).ToList();
        if (!save()) { state.Schedules = previous; return; }
        Refresh(); ResetEditor(); status("Schedule deleted.");
    }

    public sealed class DayChoice(DayOfWeek day, bool selected)
    {
        public DayOfWeek Day { get; } = day;
        public string Label => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(Day);
        public bool Selected { get; set; } = selected;
    }
}
