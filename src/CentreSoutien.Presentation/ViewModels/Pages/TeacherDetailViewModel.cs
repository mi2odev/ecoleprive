using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

public sealed record TeacherGroupRow(int GroupId, int CourseId, string Name, string When, string Fill, IRelayCommand Open);
public sealed record TeacherStudentChip(int Id, string Name, IRelayCommand Open);
public sealed record TeacherPayHistoryRow(int Id, string Month, string Date, string Method, string Amount, IRelayCommand Delete);

public sealed partial class TeacherDetailViewModel(
    ITeacherService teachers, ITeacherPaymentService teacherPayments, IDocumentService documents, ISettingsService settings, IFileStorage storage,
    INavigator nav, DialogHost dialogs, INotifier notifier, IShell shell, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    private Teacher? _teacher;

    public override string NavKey => "teachers";
    public override string Title => Name;

    public int Id => _teacher?.Id ?? 0;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _initials = "";
    [ObservableProperty] private string? _photoPath;
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private Badge _state = new("", BadgeKind.Neutral);
    [ObservableProperty] private bool _isActive;

    [ObservableProperty] private IReadOnlyList<TeacherGroupRow> _groups = [];
    [ObservableProperty] private bool _hasNoGroups;
    [ObservableProperty] private string _studentsTitle = "Élèves";
    [ObservableProperty] private IReadOnlyList<TeacherStudentChip> _students = [];
    [ObservableProperty] private IReadOnlyList<DocumentRow> _documents = [];

    [ObservableProperty] private string _earningsTitle = "";
    [ObservableProperty] private string _earnings = "";
    [ObservableProperty] private string _rule = "";
    [ObservableProperty] private string _paidSummary = "";
    [ObservableProperty] private Badge _payState = new("", BadgeKind.Neutral);
    [ObservableProperty] private IReadOnlyList<TeacherPayHistoryRow> _history = [];
    [ObservableProperty] private IReadOnlyList<Field> _info = [];

    public override async Task LoadAsync(object? parameter)
    {
        if (parameter is not int id) return;
        await RunAsync(async () =>
        {
            var now = clock.GetLocalNow().DateTime;
            var period = Period.Of(now);
            var d = await teachers.GetDetailAsync(id, period) ?? throw new BusinessException("Enseignant introuvable.");
            var t = _teacher = d.Teacher;
            var cfg = await settings.GetAsync();

            Name = t.FullName;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Id));
            Initials = t.Initials;
            PhotoPath = t.PhotoFile is null ? null : storage.GetPath(t.PhotoFile, StorageAreas.Images);
            IsActive = t.IsActive;
            State = Badge.Active(t.IsActive);
            Subtitle = string.Join(" · ", new[] { t.Subject?.Name ?? "Sans matière", $"Au centre depuis {t.StartYear}", t.Phone }.Where(x => !string.IsNullOrWhiteSpace(x)));
            Info =
            [
                new("Téléphone", t.Phone ?? "—"),
                new("E-mail", t.Email ?? "—"),
                new("Adresse", t.Address ?? "—"),
                new("Rémunération", TeacherEarnings.RuleLabel(t, Money.Format)),
            ];

            var today = now.Date;
            Groups = d.Groups.Where(g => g.IsActive).Select(g =>
            {
                var rooms = g.Room?.Name ?? "Sans salle";
                return new TeacherGroupRow(g.Id, g.CourseId, g.FullName,
                    $"{(g.Slots.Count == 0 ? "Horaire à définir" : Labels.Slots(g.Slots))} · {rooms}",
                    $"{g.Enrollments.Count(e => e.IsActiveOn(today))} / {g.Capacity}",
                    new AsyncRelayCommand(() => nav.NavigateAsync<CourseDetailViewModel>(new CourseDetailViewModel.Target(g.CourseId, g.Id))));
            }).ToList();
            HasNoGroups = Groups.Count == 0;

            StudentsTitle = $"Élèves ({d.Students.Count})";
            Students = d.Students.Select(s => new TeacherStudentChip(s.Id, s.FullName,
                new AsyncRelayCommand(() => nav.NavigateAsync<StudentDetailViewModel>(s.Id)))).ToList();

            EarningsTitle = $"Gains · {Labels.Month(period)}";
            Earnings = Money.Format(d.Earnings.Amount);
            Rule = d.Earnings.Rule;
            var remaining = Math.Max(0, d.Earnings.Amount - d.PaidThisMonth);
            PaidSummary = d.PaidThisMonth > 0
                ? $"Versé {Money.Format(d.PaidThisMonth)}{(remaining > 0 ? " · reste " + Money.Format(remaining) : "")}"
                : "Aucun versement ce mois-ci";
            var payDay = Math.Min(Math.Max(1, cfg.TeacherPayDay), DateTime.DaysInMonth(period.Year, period.Month));
            PayState = d.Earnings.Amount <= 0 ? new Badge("Rien à payer", BadgeKind.Neutral)
                : remaining <= 0 ? new Badge("Payé", BadgeKind.Ok)
                : new Badge($"À payer le {payDay:00}/{period.Month:00}", BadgeKind.Warn);

            History = d.Payments.Select(p => new TeacherPayHistoryRow(p.Id, Labels.Month(p.Period), p.Date.ToString("dd/MM/yyyy"), Labels.Of(p.Method),
                Money.Format(p.Amount), new AsyncRelayCommand(() => DeletePaymentAsync(p)))).ToList();

            await LoadDocumentsAsync();
        }, notifier);
    }

    private async Task LoadDocumentsAsync()
    {
        if (_teacher is null) return;
        var docs = await documents.ListAsync(DocumentOwnerType.Teacher, _teacher.Id);
        Documents = docs.Select(d => new DocumentRow(d.Title, d.Category ?? "—", d.CreatedAt.ToString("dd/MM/yyyy"), StudentDetailViewModel.FormatSize(d.SizeBytes),
            new RelayCommand(() => shell.Open(documents.GetFullPath(d))),
            new AsyncRelayCommand(async () =>
            {
                if (!await dialogs.ConfirmAsync("Supprimer le document", $"Supprimer « {d.Title} » ? Le fichier sera effacé.")) return;
                await RunAsync(async () => { await documents.DeleteAsync(d.Id); await LoadDocumentsAsync(); }, notifier);
            }))).ToList();
    }

    [RelayCommand]
    private async Task RecordPayment()
    {
        if (_teacher is null) return;
        var dialog = services.GetRequiredService<TeacherPaymentDialogViewModel>();
        await dialog.InitializeAsync(Id);
        if (await dialogs.ShowAsync(dialog)) await RefreshAsync();
    }

    [RelayCommand]
    private async Task Edit()
    {
        if (_teacher is null) return;
        var dialog = services.GetRequiredService<TeacherEditorDialogViewModel>();
        await dialog.InitializeAsync(Id);
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Fiche enseignant enregistrée");
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private Task OpenSchedule() => nav.NavigateAsync<ScheduleViewModel>(Id);

    [RelayCommand]
    private async Task AddDocument()
    {
        if (_teacher is null) return;
        var dialog = services.GetRequiredService<AddDocumentDialogViewModel>();
        dialog.Initialize(DocumentOwnerType.Teacher, Id, Name);
        if (!dialog.HasFile) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Document ajouté");
            await LoadDocumentsAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleActive()
    {
        if (_teacher is null) return;
        var deactivate = _teacher.IsActive;
        if (deactivate && !await dialogs.ConfirmAsync("Rendre inactif",
                $"{Name} n'apparaîtra plus dans les paiements enseignants du mois. Ses groupes restent attribués : pensez à les confier à un autre enseignant.", "Rendre inactif"))
            return;
        await RunAsync(async () =>
        {
            var t = await teachers.GetAsync(Id) ?? throw new BusinessException("Enseignant introuvable.");
            t.IsActive = !deactivate;
            await teachers.SaveAsync(t);
            notifier.Info(deactivate ? "Enseignant rendu inactif" : "Enseignant réactivé");
            await RefreshAsync();
        }, notifier);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (_teacher is null) return;
        if (!await dialogs.ConfirmAsync("Supprimer l'enseignant", $"Supprimer définitivement {Name} ? Ses groupes resteront sans enseignant. Cette action est irréversible.")) return;
        if (await RunAsync(() => teachers.DeleteAsync(Id), notifier))
        {
            notifier.Info("Enseignant supprimé");
            await nav.NavigateAsync<TeachersViewModel>();
        }
    }

    [RelayCommand]
    private Task Back() => nav.CanGoBack ? nav.BackAsync() : nav.NavigateAsync<TeachersViewModel>();

    private async Task DeletePaymentAsync(TeacherPayment p)
    {
        if (!await dialogs.ConfirmAsync("Supprimer le paiement",
                $"Supprimer le versement de {Money.Format(p.Amount)} du {p.Date:dd/MM/yyyy} ({Labels.Month(p.Period)}) ?", "Supprimer le paiement")) return;
        await RunAsync(async () =>
        {
            await teacherPayments.DeleteAsync(p.Id);
            notifier.Info("Paiement supprimé");
            await RefreshAsync();
        }, notifier);
    }
}
