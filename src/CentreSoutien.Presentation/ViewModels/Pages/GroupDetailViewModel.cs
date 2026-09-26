using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

public sealed record GroupStudentRow(int Id, string Name, string Matricule, Badge State, IRelayCommand Open, IRelayCommand Remove);

/// <summary>
/// Everything about one group: price, teacher, room, timetable, students, revenue and attendance.
/// (Groups carry their subject, level and price directly — there is no separate "course" level.)
/// </summary>
public sealed partial class GroupDetailViewModel(
    IGroupService groups, IStudentService students, ITeacherService teachers, ICrudService<Room> rooms,
    INavigator nav, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    /// <summary>Navigation parameter (a plain <c>int</c> group id works too).</summary>
    public sealed record Target(int GroupId);

    private GroupDetail? _detail;
    private Group? _group;
    private Dictionary<int, StudentListItem> _students = [];
    private bool _loading;

    public override string NavKey => "groups";
    public override string Title => string.IsNullOrEmpty(Name) ? "Groupe" : Name;

    public int? GroupId => _group?.Id;
    public bool HasGroup => _group is not null;

    /// <summary>The last immediate save started by a field change (price, teacher, room); awaited by tests.</summary>
    public Task Saving { get; private set; } = Task.CompletedTask;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isInactive;

    [ObservableProperty] private string _priceLabel = "Prix mensuel";
    [ObservableProperty] private string _price = "";
    [ObservableProperty] private IReadOnlyList<Option<int?>> _teacherOptions = [];
    [ObservableProperty] private Option<int?>? _selectedTeacher;
    [ObservableProperty] private IReadOnlyList<Option<int?>> _roomOptions = [];
    [ObservableProperty] private Option<int?>? _selectedRoom;
    [ObservableProperty] private string _schedule = "—";

    [ObservableProperty] private string _fill = "";
    [ObservableProperty] private IReadOnlyList<GroupStudentRow> _enrolled = [];
    [ObservableProperty] private IReadOnlyList<Option<int>> _eligible = [];
    [ObservableProperty] private Option<int>? _selectedEligible;

    [ObservableProperty] private string _revenueTitle = "Recettes";
    [ObservableProperty] private string _collected = "";
    [ObservableProperty] private string _expected = "";
    [ObservableProperty] private string _attendanceRate = "—";
    [ObservableProperty] private double _capacityShare;
    [ObservableProperty] private double _capacityRest = 1;
    [ObservableProperty] private bool _isFull;

    public override async Task LoadAsync(object? parameter)
    {
        var id = parameter switch
        {
            Target t => t.GroupId,
            int i => i,
            _ => (int?)null,
        };
        if (id is null) return;
        _loading = true;
        try
        {
            await RunAsync(async () =>
            {
                var now = clock.GetLocalNow().DateTime;
                var d = await groups.GetDetailAsync(id.Value, now) ?? throw new BusinessException("Groupe introuvable.");
                _detail = d;
                _group = d.Group;
                var g = d.Group;
                LastParameter = new Target(g.Id);
                OnPropertyChanged(nameof(GroupId));
                OnPropertyChanged(nameof(HasGroup));
                _students = (await students.ListAsync(now)).ToDictionary(s => s.Id);

                Name = g.FullName;
                OnPropertyChanged(nameof(Title));
                IsInactive = !g.IsActive;
                PriceLabel = $"Prix mensuel ({Money.Currency})";
                Price = Money.Number(g.MonthlyPrice);

                RevenueTitle = "Recettes · " + Labels.Month(Period.Of(now));
                Collected = Money.Format(d.Collected);
                Expected = $"sur {Money.Format(d.Expected)} attendus";
                AttendanceRate = g.Sessions.Count > 0 ? $"{Math.Round(d.AttendanceRate)} %" : "—";
                Eligible = d.EligibleStudents.Select(s => new Option<int>(s.Id, $"{s.FullName} · {s.Matricule}")).ToList();
                SelectedEligible = null;

                TeacherOptions = [new Option<int?>(null, "Aucun"), .. (await teachers.ListAsync(now))
                    .Where(t => t.IsActive || t.Id == g.TeacherId).Select(t => new Option<int?>(t.Id, t.FullName))];
                RoomOptions = [new Option<int?>(null, "Aucune"), .. (await rooms.ListAsync())
                    .Where(r => r.IsActive || r.Id == g.RoomId).Select(r => new Option<int?>(r.Id, r.Name))];
                SelectedTeacher = TeacherOptions.FirstOrDefault(t => t.Value == g.TeacherId) ?? TeacherOptions[0];
                SelectedRoom = RoomOptions.FirstOrDefault(r => r.Value == g.RoomId) ?? RoomOptions[0];

                Schedule = g.Slots.Count == 0 ? "Non planifié" : Labels.Slots(g.Slots);
                Subtitle = string.Join(" · ", new[]
                {
                    g.Teacher?.FullName ?? "Sans enseignant", g.Room?.Name ?? "Sans salle", Schedule, Money.Format(g.MonthlyPrice) + " / mois",
                });

                Enrolled = d.Students
                    .Select(s => _students.GetValueOrDefault(s.Id) is { } row
                        ? new GroupStudentRow(row.Id, row.FullName, row.Matricule, Badge.For(row.State),
                            new AsyncRelayCommand(() => nav.NavigateAsync<StudentDetailViewModel>(row.Id)),
                            new AsyncRelayCommand(() => RemoveStudentAsync(row.Id, row.FullName)))
                        : null)
                    .OfType<GroupStudentRow>()
                    .ToList();
                var n = Enrolled.Count;
                Fill = $"{n} / {g.Capacity}";
                IsFull = n >= g.Capacity;
                CapacityShare = g.Capacity == 0 ? 1 : Math.Min(1, n / (double)g.Capacity);
                CapacityRest = 1 - CapacityShare;
            }, notifier);
        }
        finally
        {
            _loading = false;
        }
    }

    // ===== Immediate saves (price, teacher, room) =====

    partial void OnPriceChanged(string value)
    {
        if (!_loading) Saving = SavePriceAsync();
    }

    partial void OnSelectedTeacherChanged(Option<int?>? value)
    {
        if (_loading || _group is null || value is null || value.Value == _group.TeacherId) return;
        Saving = SaveGroupAsync(g => g.TeacherId = value.Value, "Enseignant du groupe modifié");
    }

    partial void OnSelectedRoomChanged(Option<int?>? value)
    {
        if (_loading || _group is null || value is null || value.Value == _group.RoomId) return;
        Saving = SaveGroupAsync(g => g.RoomId = value.Value, "Salle modifiée");
    }

    [RelayCommand]
    private Task SavePrice() => Saving.IsCompleted ? Saving = SavePriceAsync() : Saving;

    private async Task SavePriceAsync()
    {
        if (_group is null) return;
        var price = Parse.Amount(Price);
        if (price is null || price < 0)
        {
            notifier.Error("Saisissez un prix mensuel valide.");
            return;
        }
        if (price == _group.MonthlyPrice) return;
        await SaveGroupAsync(g => g.MonthlyPrice = price.Value, "Prix mis à jour");
    }

    private async Task SaveGroupAsync(Action<Group> change, string message)
    {
        var g = _group;
        if (g is null) return;
        var ok = await RunAsync(async () =>
        {
            change(g);
            await groups.SaveAsync(g, g.Slots);
        }, notifier);
        if (ok) notifier.Info(message);
        // Reload in both cases: shows the new state, or reverts the selection after a refused change (conflict).
        var error = Error;
        await RefreshAsync();
        if (!ok) Error = error;
    }

    // ===== Students =====

    [RelayCommand]
    private async Task AddStudent()
    {
        if (_group is null) return;
        if (SelectedEligible is not { } s)
        {
            notifier.Error("Choisissez un élève à ajouter.");
            return;
        }
        if (await RunAsync(() => students.EnrollAsync(s.Value, _group.Id), notifier))
        {
            notifier.Info($"{_students.GetValueOrDefault(s.Value)?.FullName ?? "Élève"} inscrit au groupe {_group.FullName}");
            await RefreshAsync();
        }
    }

    private async Task RemoveStudentAsync(int studentId, string name)
    {
        if (_group is not { } g) return;
        if (!await dialogs.ConfirmAsync("Retirer du groupe", $"Retirer {name} de {g.FullName} ? L'historique (présences, paiements) est conservé.", "Retirer")) return;
        if (await RunAsync(() => students.UnenrollAsync(studentId, g.Id), notifier))
        {
            notifier.Info($"{name} retiré du groupe");
            await RefreshAsync();
        }
    }

    // ===== Group =====

    /// <summary>Opens the group form (subject, level, price, name, teacher, room, capacity, timetable).</summary>
    [RelayCommand]
    private async Task EditGroup()
    {
        if (_group is null) return;
        var dialog = services.GetRequiredService<GroupEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(_group.Id), notifier)) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Groupe enregistré");
            await RefreshAsync();
        }
    }

    /// <summary>Creates another group of the same subject and level (e.g. group B), pre-filled from this one.</summary>
    [RelayCommand]
    private async Task NewGroup()
    {
        var dialog = services.GetRequiredService<GroupEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(null, _group), notifier)) return;
        if (await dialogs.ShowAsync(dialog) && dialog.SavedId is { } id)
        {
            notifier.Info("Groupe créé");
            await nav.NavigateAsync<GroupDetailViewModel>(new Target(id));
        }
    }

    [RelayCommand]
    private async Task DeleteGroup()
    {
        if (_group is not { } g) return;
        if (!await dialogs.ConfirmAsync("Supprimer le groupe", $"Supprimer {g.FullName} et son emploi du temps ?")) return;
        if (await RunAsync(() => groups.DeleteAsync(g.Id), notifier))
        {
            notifier.Info("Groupe supprimé");
            await nav.NavigateAsync<GroupsViewModel>();
        }
    }

    [RelayCommand]
    private Task OpenAttendance() => nav.NavigateAsync<AttendanceViewModel>(new AttendanceViewModel.Target(clock.GetLocalNow().Date, _group?.Id));

    [RelayCommand]
    private Task Back() => nav.CanGoBack ? nav.BackAsync() : nav.NavigateAsync<GroupsViewModel>();
}
