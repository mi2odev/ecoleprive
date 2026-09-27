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

public sealed record EnrollmentRow(int GroupId, int CourseId, string Name, string Details, string Price, IRelayCommand Open, IRelayCommand Change, IRelayCommand Remove);
public sealed record AttendanceHistoryRow(string Date, string Group, Badge Status);
public sealed record GradeRow(string Group, string Scores, string Average, bool Passing);
public sealed record PaymentHistoryRow(string Receipt, string Date, string Kind, string Period, string Method, string Amount, IRelayCommand Print, IRelayCommand Delete);
public sealed record DocumentRow(string Title, string Category, string Date, string Size, IRelayCommand Open, IRelayCommand Delete);

public sealed partial class StudentDetailViewModel(
    IStudentService students, IDocumentService documents, IPaymentService payments, ISettingsService settings, IFileStorage storage,
    INavigator nav, DialogHost dialogs, INotifier notifier, IShell shell, IPrintService printer,
    TimeProvider clock, IServiceProvider services) : PageViewModel
{
    private Student? _student;

    public override string NavKey => "students";
    public override string Title => Name;

    public int Id => _student?.Id ?? 0;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _initials = "";
    [ObservableProperty] private string? _photoPath;
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private Badge _state = new("", BadgeKind.Neutral);
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _tab = "overview";

    [ObservableProperty] private IReadOnlyList<EnrollmentRow> _enrollments = [];
    [ObservableProperty] private bool _hasNoEnrollment;
    [ObservableProperty] private IReadOnlyList<Field> _info = [];
    [ObservableProperty] private string _monthTitle = "";
    [ObservableProperty] private IReadOnlyList<Field> _monthFinance = [];
    [ObservableProperty] private string _parentName = "Aucun parent lié";
    [ObservableProperty] private string _parentPhone = "";
    [ObservableProperty] private bool _hasParent;

    [ObservableProperty] private string _attendanceRate = "—";
    [ObservableProperty] private IReadOnlyList<AttendanceHistoryRow> _attendance = [];
    [ObservableProperty] private IReadOnlyList<GradeRow> _grades = [];
    [ObservableProperty] private IReadOnlyList<PaymentHistoryRow> _payments = [];
    [ObservableProperty] private IReadOnlyList<DocumentRow> _documents = [];

    public bool IsOverview => Tab == "overview";
    public bool IsAttendance => Tab == "attendance";
    public bool IsGrades => Tab == "grades";
    public bool IsPayments => Tab == "payments";
    public bool IsDocuments => Tab == "documents";

    partial void OnTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsOverview));
        OnPropertyChanged(nameof(IsAttendance));
        OnPropertyChanged(nameof(IsGrades));
        OnPropertyChanged(nameof(IsPayments));
        OnPropertyChanged(nameof(IsDocuments));
    }

    [RelayCommand] private void ShowTab(string tab) => Tab = tab;

    public override async Task LoadAsync(object? parameter)
    {
        if (parameter is not int id) return;
        await RunAsync(async () =>
        {
            var s = await students.GetAsync(id) ?? throw new BusinessException("Élève introuvable.");
            _student = s;
            var now = clock.GetLocalNow().DateTime;
            var period = Period.Of(now);

            Name = s.FullName;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Id));
            Initials = s.Initials;
            PhotoPath = s.PhotoFile is null ? null : storage.GetPath(s.PhotoFile, "images");
            IsActive = s.IsActive;
            Subtitle = string.Join(" · ", new[] { s.Matricule, s.Level, "Inscrit le " + s.EnrolledOn.ToString("dd/MM/yyyy"), s.School }.Where(x => !string.IsNullOrWhiteSpace(x)));
            State = Badge.For(Billing.State(s, now));

            var today = now.Date;
            Enrollments = s.Enrollments.Where(e => e.IsActiveOn(today) || e.StartDate > today).Select(e =>
            {
                var g = e.Group!;
                var pack = Packs.Status(e, now);
                return new EnrollmentRow(g.Id, g.Id, g.FullName,
                    $"{g.Teacher?.FullName ?? "Sans enseignant"} · {Labels.Slots(g.Slots)} · séance {Math.Min(pack.Done + 1, pack.Size)}/{pack.Size}",
                    g.PriceLabel,
                    new AsyncRelayCommand(() => nav.NavigateAsync<GroupDetailViewModel>(new GroupDetailViewModel.Target(g.Id))),
                    new AsyncRelayCommand(() => ChangeGroupAsync(g.Id)),
                    new AsyncRelayCommand(() => UnenrollAsync(g)));
            }).ToList();
            HasNoEnrollment = Enrollments.Count == 0;

            Info =
            [
                new("Date de naissance", s.BirthDate?.ToString("dd/MM/yyyy") ?? "—"),
                new("Établissement", s.School ?? "—"),
                new("Téléphone", s.Phone ?? "—"),
                new("Adresse", s.Address ?? s.Parent?.Address ?? "—"),
                new("Matricule", s.Matricule),
                new("Remise", s.Discount?.ToString() ?? "Aucune"),
            ];

            // Each group is paid on its own, by packs of sessions (at joining, then every N sessions).
            MonthTitle = "Paiement des séances";
            var accounts = Billing.Accounts(s, now);
            var balance = accounts.Sum(a => a.Balance);
            MonthFinance =
            [
                .. accounts.Select(a => new Field(a.Group, string.Join(" · ", new[]
                {
                    a.Status is { } st ? $"séance {Math.Min(st.Done + 1, st.Size)}/{st.Size}" : "a quitté",
                    a.Balance > 0 ? "reste " + Money.Format(a.Balance) : a.Credit > 0 ? Money.Format(a.Credit) + " d'avance" : "payé",
                }))),
                new("Reste à payer", Money.Format(balance)),
            ];
            HasParent = s.Parent is not null;
            ParentName = s.Parent is null ? "Aucun parent lié" : $"{s.Parent.FullName}{(string.IsNullOrWhiteSpace(s.Parent.Relation) ? "" : " · " + s.Parent.Relation)}";
            ParentPhone = s.Parent?.Phone ?? "";

            var hist = await students.AttendanceHistoryAsync(id);
            Attendance = hist.Select(h => new AttendanceHistoryRow(h.Date.ToString("dd/MM/yyyy"), h.Group, Badge.For(h.Status))).ToList();
            AttendanceRate = hist.Count == 0 ? "—" : $"{Math.Round(hist.Count(h => h.Status != AttendanceStatus.Absent) * 100.0 / hist.Count)} %";

            var cfg = await settings.GetAsync();
            Grades = (await students.GradesAsync(id)).Select(g => new GradeRow(g.Group,
                string.Join("   ", g.Scores.Select(x => $"{x.Exam} : {(x.Score is null ? "—" : $"{x.Score:0.##}/{x.Max:0.##}")}")),
                g.Average is null ? "—" : g.Average.Value.ToString("0.00"), g.Average is null || g.Average >= cfg.PassingGrade)).ToList();

            Payments = s.Payments.OrderByDescending(p => p.Date).Select(p => new PaymentHistoryRow(
                p.ReceiptNumber, p.Date.ToString("dd/MM/yyyy"), p.Group?.FullName ?? Labels.Of(p.Kind), Labels.Month(p.Period), Labels.Of(p.Method), Money.Format(p.Amount),
                new AsyncRelayCommand(() => PrintReceiptAsync(p.Id)),
                new AsyncRelayCommand(() => DeletePaymentAsync(p)))).ToList();

            await LoadDocumentsAsync();
        }, notifier);
    }

    private async Task LoadDocumentsAsync()
    {
        var docs = await documents.ListAsync(DocumentOwnerType.Student, Id);
        Documents = docs.Select(d => new DocumentRow(d.Title, d.Category ?? "—", d.CreatedAt.ToString("dd/MM/yyyy"), FormatSize(d.SizeBytes),
            new RelayCommand(() => shell.Open(documents.GetFullPath(d))),
            new AsyncRelayCommand(async () =>
            {
                if (!await dialogs.ConfirmAsync("Supprimer le document", $"Supprimer « {d.Title} » ? Le fichier sera effacé.")) return;
                await RunAsync(async () => { await documents.DeleteAsync(d.Id); await LoadDocumentsAsync(); }, notifier);
            }))).ToList();
    }

    public static string FormatSize(long bytes) => bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} Ko" : $"{bytes / 1024.0 / 1024.0:0.0} Mo";

    [RelayCommand]
    private async Task AddPayment()
    {
        var dialog = services.GetRequiredService<CollectPaymentDialogViewModel>();
        await dialog.InitializeAsync(Id);
        if (await dialogs.ShowAsync(dialog)) await RefreshAsync();
    }

    [RelayCommand]
    private async Task Edit()
    {
        var dialog = services.GetRequiredService<StudentEditorDialogViewModel>();
        await dialog.InitializeAsync(Id);
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Fiche élève enregistrée");
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task Enroll()
    {
        var dialog = services.GetRequiredService<EnrollDialogViewModel>();
        await dialog.InitializeAsync(Id, null);
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Inscription enregistrée");
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private Task ChangeGroup() => ChangeGroupAsync(Enrollments.FirstOrDefault()?.GroupId);

    private async Task ChangeGroupAsync(int? fromGroupId)
    {
        if (fromGroupId is null)
        {
            await Enroll();
            return;
        }
        var dialog = services.GetRequiredService<EnrollDialogViewModel>();
        await dialog.InitializeAsync(Id, fromGroupId);
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Changement de groupe enregistré");
            await RefreshAsync();
        }
    }

    private async Task UnenrollAsync(Group g)
    {
        if (!await dialogs.ConfirmAsync("Retirer du groupe", $"Retirer {Name} de {g.FullName} ? L'historique (présences, paiements) est conservé.", "Retirer")) return;
        await RunAsync(async () =>
        {
            await students.UnenrollAsync(Id, g.Id);
            notifier.Info($"{Name} retiré de {g.FullName}");
            await RefreshAsync();
        }, notifier);
    }

    [RelayCommand]
    private async Task ApplyDiscount()
    {
        var dialog = services.GetRequiredService<DiscountPickerDialogViewModel>();
        await dialog.InitializeAsync(Id, _student?.DiscountId);
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Remise mise à jour");
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task AddDocument()
    {
        var dialog = services.GetRequiredService<AddDocumentDialogViewModel>();
        dialog.Initialize(DocumentOwnerType.Student, Id, Name);
        if (!dialog.HasFile) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Document ajouté");
            Tab = "documents";
            await LoadDocumentsAsync();
        }
    }

    [RelayCommand]
    private async Task PrintDocuments()
    {
        var dialog = services.GetRequiredService<PrintDocumentsDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeForStudentAsync(Id), notifier)) return;
        if (await dialogs.ShowAsync(dialog)) notifier.Info("Document envoyé à l'impression");
    }

    [RelayCommand]
    private async Task ToggleActive()
    {
        if (_student is null) return;
        var deactivate = _student.IsActive;
        if (deactivate && !await dialogs.ConfirmAsync("Rendre inactif", $"{Name} ne sera plus facturé ni compté dans les effectifs. Ses inscriptions en cours seront terminées.", "Rendre inactif"))
            return;
        await RunAsync(async () =>
        {
            if (deactivate)
                foreach (var e in Enrollments) await students.UnenrollAsync(Id, e.GroupId);
            _student.IsActive = !deactivate;
            await students.SaveAsync(_student);
            notifier.Info(deactivate ? "Élève rendu inactif" : "Élève réactivé");
            await RefreshAsync();
        }, notifier);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (!await dialogs.ConfirmAsync("Supprimer l'élève", $"Supprimer définitivement {Name} et tout son historique ? Cette action est irréversible.")) return;
        if (await RunAsync(() => students.DeleteAsync(Id), notifier))
        {
            notifier.Info("Élève supprimé");
            await nav.NavigateAsync<StudentsViewModel>();
        }
    }

    [RelayCommand]
    private Task OpenParent() => _student?.ParentId is { } pid ? nav.NavigateAsync<ParentsViewModel>(pid) : Task.CompletedTask;

    [RelayCommand]
    private Task EnterGrades() => nav.NavigateAsync<GradesViewModel>(Enrollments.FirstOrDefault()?.GroupId);

    [RelayCommand]
    private Task Back() => nav.CanGoBack ? nav.BackAsync() : nav.NavigateAsync<StudentsViewModel>();

    private async Task PrintReceiptAsync(int paymentId)
    {
        await RunAsync(async () =>
        {
            var p = await payments.GetReceiptAsync(paymentId) ?? throw new BusinessException("Reçu introuvable.");
            var cfg = await settings.GetAsync();
            printer.PrintReceipt(p, cfg, cfg.LogoFile is null ? null : storage.GetPath(cfg.LogoFile, "images"), duplicate: true);
        }, notifier);
    }

    private async Task DeletePaymentAsync(StudentPayment p)
    {
        p.Student ??= _student;
        var dialog = services.GetRequiredService<CancelReceiptDialogViewModel>();
        dialog.Initialize(p);
        if (await dialogs.ShowAsync(dialog)) await RefreshAsync();
    }
}
