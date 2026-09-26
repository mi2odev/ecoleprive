using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

public sealed record SessionRow(
    int Id, DateTime Date, string Time, string Group, string Teacher, string Room, string? Topic, Badge Status, string Attendance,
    bool IsCancelled, IRelayCommand TakeAttendance, IRelayCommand Edit, IRelayCommand ToggleCancel, IRelayCommand Delete)
{
    public string ToggleLabel => IsCancelled ? "Rétablir" : "Annuler";
    public bool HasTopic => !string.IsNullOrWhiteSpace(Topic);
}

public sealed record SessionDayGroup(string Title, string Count, IReadOnlyList<SessionRow> Rows);

public sealed partial class SessionsViewModel(
    ISessionService sessions, INavigator nav, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel, IHasPrimaryAction
{
    private List<Session> _all = [];

    public override string NavKey => "sessions";
    public override string Title => "Séances";

    public IReadOnlyList<Option<SessionStatus?>> StatusFilters { get; } =
        [new(null, "Toutes"), new(SessionStatus.Planned, "Prévues"), new(SessionStatus.Done, "Effectuées"), new(SessionStatus.Cancelled, "Annulées")];

    [ObservableProperty] private DateTime _weekStart;
    [ObservableProperty] private string _weekLabel = "";
    [ObservableProperty] private Option<SessionStatus?>? _selectedStatus;
    [ObservableProperty] private IReadOnlyList<SessionDayGroup> _days = [];
    [ObservableProperty] private int _sessionCount;
    [ObservableProperty] private string _countLabel = "";
    [ObservableProperty] private bool _isEmpty;
    /// <summary>No session at all this week (before the status filter).</summary>
    [ObservableProperty] private bool _hasNoData;
    /// <summary>The week has sessions but the status filter hides all of them.</summary>
    [ObservableProperty] private bool _hasNoResults;

    partial void OnSelectedStatusChanged(Option<SessionStatus?>? value) => ApplyFilter();

    private DateTime Now => clock.GetLocalNow().DateTime;

    public override async Task LoadAsync(object? parameter)
    {
        if (parameter is DateTime d) WeekStart = PlanningWeek.StartOf(d);
        if (WeekStart == default) WeekStart = PlanningWeek.StartOf(Now);
        SelectedStatus ??= StatusFilters[0];
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        await RunAsync(async () =>
        {
            WeekLabel = PlanningWeek.Label(WeekStart);
            _all = await sessions.ListAsync(WeekStart, WeekStart.AddDays(6));
            ApplyFilter();
        }, notifier);
    }

    private void ApplyFilter()
    {
        var list = _all.Where(s => SelectedStatus?.Value is not { } st || s.Status == st).ToList();
        Days = list.GroupBy(s => s.Date.Date).OrderBy(g => g.Key).Select(g =>
        {
            var rows = g.OrderBy(s => s.Start).ThenBy(s => s.Group?.FullName).Select(ToRow).ToList();
            return new SessionDayGroup(PlanningWeek.DayTitle(g.Key), $"{rows.Count} séance{(rows.Count > 1 ? "s" : "")}", rows);
        }).ToList();
        SessionCount = list.Count;
        CountLabel = $"{_all.Count} séance{(_all.Count > 1 ? "s" : "")} cette semaine";
        IsEmpty = list.Count == 0;
        HasNoData = _all.Count == 0;
        HasNoResults = _all.Count > 0 && list.Count == 0;
    }

    /// <summary>Shows every status again.</summary>
    [RelayCommand]
    private void ClearFilters() => SelectedStatus = StatusFilters[0];

    private SessionRow ToRow(Session s)
    {
        var g = s.Group!;
        var enrolled = g.Enrollments.Count(e => e.IsActiveOn(s.Date));
        var marked = s.Attendance.Count;
        return new SessionRow(s.Id, s.Date, $"{Labels.Time(s.Start)}–{Labels.Time(s.End)}", g.FullName,
            s.Teacher?.FullName ?? g.Teacher?.FullName ?? "—", s.Room?.Name ?? g.Room?.Name ?? "—", s.Topic, Badge.For(s.Status),
            $"{marked} / {Math.Max(enrolled, marked)} saisis", s.Status == SessionStatus.Cancelled,
            new AsyncRelayCommand(() => nav.NavigateAsync<AttendanceViewModel>(new AttendanceViewModel.Target(s.Date, s.GroupId, s.Id))),
            new AsyncRelayCommand(() => EditAsync(s)),
            new AsyncRelayCommand(() => ToggleCancelAsync(s)),
            new AsyncRelayCommand(() => DeleteAsync(s)));
    }

    [RelayCommand]
    private Task PreviousWeek()
    {
        WeekStart = WeekStart.AddDays(-7);
        return ReloadAsync();
    }

    [RelayCommand]
    private Task NextWeek()
    {
        WeekStart = WeekStart.AddDays(7);
        return ReloadAsync();
    }

    [RelayCommand]
    private Task ThisWeek()
    {
        WeekStart = PlanningWeek.StartOf(Now);
        return ReloadAsync();
    }

    [RelayCommand]
    private async Task Generate()
    {
        var created = 0;
        if (!await RunAsync(async () => created = await sessions.GenerateAsync(WeekStart, WeekStart.AddDays(6)), notifier)) return;
        notifier.Info(created == 0 ? "Toutes les séances de la semaine existent déjà" : $"{created} séance{(created > 1 ? "s" : "")} créée{(created > 1 ? "s" : "")}");
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task Add()
    {
        var today = Now.Date;
        var date = today >= WeekStart && today < WeekStart.AddDays(7) ? today : WeekStart;
        var dialog = services.GetRequiredService<SessionEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(null, date), notifier)) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Séance créée");
            await ReloadAsync();
        }
    }

    private async Task EditAsync(Session s)
    {
        var dialog = services.GetRequiredService<SessionEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(s), notifier)) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Séance enregistrée");
            await ReloadAsync();
        }
    }

    private async Task ToggleCancelAsync(Session s)
    {
        var restore = s.Status == SessionStatus.Cancelled;
        if (!restore && !await dialogs.ConfirmAsync("Annuler la séance", $"Annuler la séance de {s.Group!.FullName} du {PlanningWeek.DayTitle(s.Date).ToLowerInvariant()} ?", "Annuler la séance"))
            return;
        var status = restore ? (s.Attendance.Count > 0 ? SessionStatus.Done : SessionStatus.Planned) : SessionStatus.Cancelled;
        if (await RunAsync(() => sessions.SetStatusAsync(s.Id, status), notifier))
        {
            notifier.Info(restore ? "Séance rétablie" : "Séance annulée");
            await ReloadAsync();
        }
    }

    private async Task DeleteAsync(Session s)
    {
        if (!await dialogs.ConfirmAsync("Supprimer la séance",
                $"Supprimer la séance de {s.Group!.FullName} ({Labels.Time(s.Start)}–{Labels.Time(s.End)}) ? Les présences saisies seront effacées.")) return;
        if (await RunAsync(() => sessions.DeleteAsync(s.Id), notifier))
        {
            notifier.Info("Séance supprimée");
            await ReloadAsync();
        }
    }

    // Ctrl+N in the shell.
    IAsyncRelayCommand? IHasPrimaryAction.PrimaryCommand => AddCommand;
    string? IHasPrimaryAction.PrimaryLabel => "Nouvelle séance";
}
