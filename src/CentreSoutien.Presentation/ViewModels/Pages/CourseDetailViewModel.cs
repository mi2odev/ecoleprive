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

public sealed record CourseStudentRow(int Id, string Name, string Matricule, Badge State, IRelayCommand Open, IRelayCommand Remove);

public sealed partial class CourseDetailViewModel(
    ICourseService courses, IGroupService groups, IStudentService students, ITeacherService teachers, ICrudService<Room> rooms,
    INavigator nav, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    /// <summary>Navigation parameter: course id and optionally the group to select.</summary>
    public sealed record Target(int CourseId, int? GroupId = null);

    private CourseDetail? _detail;
    private Group? _group;
    private Dictionary<int, StudentListItem> _students = [];
    private bool _loading;

    public override string NavKey => "courses";
    public override string Title => string.IsNullOrEmpty(Name) ? "Cours" : Name;

    public int CourseId => _detail?.Course.Id ?? 0;
    public int? GroupId => _group?.Id;

    /// <summary>The last immediate save started by a field change (price, teacher, room); awaited by tests.</summary>
    public Task Saving { get; private set; } = Task.CompletedTask;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isInactive;
    [ObservableProperty] private IReadOnlyList<Option<int>> _groupOptions = [];
    [ObservableProperty] private Option<int>? _selectedGroup;
    [ObservableProperty] private bool _hasGroup;

    [ObservableProperty] private string _priceLabel = "Prix mensuel";
    [ObservableProperty] private string _price = "";
    [ObservableProperty] private IReadOnlyList<Option<int?>> _teacherOptions = [];
    [ObservableProperty] private Option<int?>? _selectedTeacher;
    [ObservableProperty] private IReadOnlyList<Option<int?>> _roomOptions = [];
    [ObservableProperty] private Option<int?>? _selectedRoom;
    [ObservableProperty] private string _schedule = "—";

    [ObservableProperty] private string _fill = "";
    [ObservableProperty] private IReadOnlyList<CourseStudentRow> _enrolled = [];
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
        var target = parameter switch
        {
            Target t => t,
            int id => new Target(id),
            _ => null,
        };
        if (target is null) return;
        _loading = true;
        try
        {
            await RunAsync(async () =>
            {
                var now = clock.GetLocalNow().DateTime;
                var d = await courses.GetDetailAsync(target.CourseId, now) ?? throw new BusinessException("Cours introuvable.");
                _detail = d;
                _students = (await students.ListAsync(now)).ToDictionary(s => s.Id);

                Name = d.Course.Name;
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(CourseId));
                IsInactive = !d.Course.IsActive;
                PriceLabel = $"Prix mensuel ({Money.Currency})";
                Price = Money.Number(d.Course.MonthlyPrice);

                var period = Period.Of(now);
                RevenueTitle = "Recettes · " + Labels.Month(period);
                Collected = Money.Format(d.Collected);
                Expected = $"sur {Money.Format(d.Expected)} attendus";
                AttendanceRate = d.Groups.Any(g => g.Sessions.Count > 0) ? $"{Math.Round(d.AttendanceRate)} %" : "—";
                Eligible = d.EligibleStudents.Select(s => new Option<int>(s.Id, $"{s.FullName} · {s.Matricule}")).ToList();
                SelectedEligible = null;

                var groupTeacherIds = d.Groups.Select(g => g.TeacherId).ToHashSet();
                TeacherOptions = [new Option<int?>(null, "Aucun"), .. (await teachers.ListAsync(now))
                    .Where(t => t.IsActive || groupTeacherIds.Contains(t.Id)).Select(t => new Option<int?>(t.Id, t.FullName))];
                var groupRoomIds = d.Groups.Select(g => g.RoomId).ToHashSet();
                RoomOptions = [new Option<int?>(null, "Aucune"), .. (await rooms.ListAsync())
                    .Where(r => r.IsActive || groupRoomIds.Contains(r.Id)).Select(r => new Option<int?>(r.Id, r.Name))];

                GroupOptions = d.Groups.Select(g => new Option<int>(g.Id, g.IsActive ? g.Name : g.Name + " (inactif)")).ToList();
                SelectedGroup = GroupOptions.FirstOrDefault(o => o.Value == target.GroupId)
                    ?? GroupOptions.FirstOrDefault(o => d.Groups.First(g => g.Id == o.Value).IsActive)
                    ?? GroupOptions.FirstOrDefault();
                ApplyGroup();
            }, notifier);
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnSelectedGroupChanged(Option<int>? value)
    {
        if (_loading) return;
        _loading = true;
        try { ApplyGroup(); }
        finally { _loading = false; }
    }

    /// <summary>Fills the group-dependent fields from the loaded detail (no database access).</summary>
    private void ApplyGroup()
    {
        var d = _detail;
        if (d is null) return;
        _group = d.Groups.FirstOrDefault(g => g.Id == SelectedGroup?.Value);
        OnPropertyChanged(nameof(GroupId));
        HasGroup = _group is not null;
        LastParameter = new Target(d.Course.Id, _group?.Id);
        var today = clock.GetLocalNow().Date;

        if (_group is not { } g)
        {
            Subtitle = d.Groups.Count == 0 ? "Aucun groupe · créez un groupe pour planifier ce cours" : "";
            SelectedTeacher = TeacherOptions.FirstOrDefault();
            SelectedRoom = RoomOptions.FirstOrDefault();
            Schedule = "—";
            Fill = "0 / 0";
            Enrolled = [];
            CapacityShare = 0;
            CapacityRest = 1;
            IsFull = false;
            return;
        }

        Schedule = g.Slots.Count == 0 ? "Non planifié" : Labels.Slots(g.Slots);
        Subtitle = string.Join(" · ", new[]
        {
            $"Groupe {g.Name}", g.Teacher?.FullName ?? "Sans enseignant", g.Room?.Name ?? "Sans salle", Schedule,
        });
        SelectedTeacher = TeacherOptions.FirstOrDefault(t => t.Value == g.TeacherId) ?? TeacherOptions[0];
        SelectedRoom = RoomOptions.FirstOrDefault(r => r.Value == g.RoomId) ?? RoomOptions[0];

        var active = g.Enrollments.Where(e => e.IsActiveOn(today) || e.StartDate.Date > today).ToList();
        Enrolled = active
            .Select(e => _students.GetValueOrDefault(e.StudentId) is { } s
                ? new CourseStudentRow(s.Id, s.FullName, s.Matricule, Badge.For(s.State),
                    new AsyncRelayCommand(() => nav.NavigateAsync<StudentDetailViewModel>(s.Id)),
                    new AsyncRelayCommand(() => RemoveStudentAsync(s.Id, s.FullName)))
                : null)
            .OfType<CourseStudentRow>()
            .OrderBy(r => r.Name)
            .ToList();
        var n = active.Count;
        Fill = $"{n} / {g.Capacity}";
        IsFull = n >= g.Capacity;
        CapacityShare = g.Capacity == 0 ? 1 : Math.Min(1, n / (double)g.Capacity);
        CapacityRest = 1 - CapacityShare;
    }

    // ===== Immediate saves (price, teacher, room) =====

    partial void OnPriceChanged(string value)
    {
        if (!_loading) Saving = SavePriceAsync();
    }

    partial void OnSelectedTeacherChanged(Option<int?>? value)
    {
        if (_loading || _group is null || value is null || value.Value == _group.TeacherId) return;
        Saving = SaveGroupAsync(g => g.TeacherId = value.Value, "Enseignant du cours modifié");
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
        if (_detail is null) return;
        var course = _detail.Course;
        var price = Parse.Amount(Price);
        if (price is null || price < 0)
        {
            notifier.Error("Saisissez un prix mensuel valide.");
            return;
        }
        if (price == course.MonthlyPrice) return;
        var old = course.MonthlyPrice;
        course.MonthlyPrice = price.Value;
        if (await RunAsync(() => courses.SaveAsync(course), notifier))
        {
            notifier.Info("Prix mis à jour");
            await RefreshAsync();
        }
        else course.MonthlyPrice = old;
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
        if (_group is null)
        {
            notifier.Error("Créez d'abord un groupe pour ce cours.");
            return;
        }
        if (SelectedEligible is not { } s)
        {
            notifier.Error("Choisissez un élève à ajouter.");
            return;
        }
        if (await RunAsync(() => students.EnrollAsync(s.Value, _group.Id), notifier))
        {
            notifier.Info($"{_students.GetValueOrDefault(s.Value)?.FullName ?? "Élève"} inscrit au groupe {_group.Name}");
            await RefreshAsync();
        }
    }

    private async Task RemoveStudentAsync(int studentId, string name)
    {
        if (_group is not { } g) return;
        if (!await dialogs.ConfirmAsync("Retirer du cours", $"Retirer {name} de {g.FullName} ? L'historique (présences, paiements) est conservé.", "Retirer")) return;
        if (await RunAsync(() => students.UnenrollAsync(studentId, g.Id), notifier))
        {
            notifier.Info($"{name} retiré du cours");
            await RefreshAsync();
        }
    }

    // ===== Groups =====

    [RelayCommand]
    private Task NewGroup() => EditGroupAsync(null);

    [RelayCommand]
    private Task EditGroup() => _group is null ? EditGroupAsync(null) : EditGroupAsync(_group.Id);

    private async Task EditGroupAsync(int? groupId)
    {
        var dialog = services.GetRequiredService<GroupEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(groupId, CourseId), notifier)) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info(groupId is null ? "Groupe créé" : "Groupe enregistré");
            await LoadAsync(new Target(CourseId, dialog.SavedId ?? groupId));
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
            await LoadAsync(new Target(CourseId));
        }
    }

    // ===== Course =====

    [RelayCommand]
    private async Task EditCourse()
    {
        var dialog = services.GetRequiredService<CourseEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(CourseId), notifier)) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Cours enregistré");
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteCourse()
    {
        if (!await dialogs.ConfirmAsync("Supprimer le cours", $"Supprimer définitivement le cours {Name} et ses groupes ?")) return;
        if (await RunAsync(() => courses.DeleteAsync(CourseId), notifier))
        {
            notifier.Info("Cours supprimé");
            await nav.NavigateAsync<CoursesViewModel>();
        }
    }

    [RelayCommand]
    private Task OpenAttendance() => nav.NavigateAsync<AttendanceViewModel>(new AttendanceViewModel.Target(clock.GetLocalNow().Date, _group?.Id));

    [RelayCommand]
    private Task Back() => nav.NavigateAsync<CoursesViewModel>();
}
