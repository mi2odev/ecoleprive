using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.Printing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CentreSoutien.Presentation.ViewModels.Dialogs;

public enum AcademicDocumentType
{
    ReportCard,
    Certificate,
    Attendance,
}

/// <summary>
/// Prints school documents: for one student (report card, enrollment certificate or attendance certificate),
/// or the report cards of every student of a group in a single print job.
/// </summary>
public sealed partial class PrintDocumentsDialogViewModel(
    IAcademicDocumentsService documents, IStudentService students, IGroupService groups, ISettingsService settings,
    IFileStorage storage, IPrintService printer, TimeProvider clock) : DialogViewModel
{
    private int? _studentId;
    private int? _groupId;
    private CenterSettings _settings = new();

    public override string Title => IsGroupMode ? "Bulletins du groupe" : "Documents imprimables";
    public override string ConfirmText => "Imprimer";
    public override double Width => 560;

    public bool IsGroupMode => _groupId is not null;
    public bool ShowTypes => !IsGroupMode;

    public IReadOnlyList<Option<AcademicDocumentType>> Types { get; } =
    [
        new(AcademicDocumentType.ReportCard, "Bulletin"),
        new(AcademicDocumentType.Certificate, "Certificat de scolarité"),
        new(AcademicDocumentType.Attendance, "Attestation de présence"),
    ];

    [ObservableProperty] private string _subjectLabel = "";
    [ObservableProperty] private string _subjectName = "";
    [ObservableProperty] private Option<AcademicDocumentType>? _selectedType;
    /// <summary>Months around today, preceded by the whole academic year (value null).</summary>
    [ObservableProperty] private IReadOnlyList<Option<DateTime?>> _periods = [];
    [ObservableProperty] private Option<DateTime?>? _selectedPeriod;
    [ObservableProperty] private string? _appreciation;
    [ObservableProperty] private string _hint = "";

    public bool ShowPeriod => SelectedType?.Value != AcademicDocumentType.Certificate;
    public bool ShowAppreciation => SelectedType?.Value == AcademicDocumentType.ReportCard;
    public string AppreciationLabel => IsGroupMode ? "Appréciation commune (facultatif)" : "Appréciation (facultatif)";

    /// <summary>Number of pages of the last print job.</summary>
    public int PrintedPageCount { get; private set; }

    partial void OnSelectedTypeChanged(Option<AcademicDocumentType>? value)
    {
        OnPropertyChanged(nameof(ShowPeriod));
        OnPropertyChanged(nameof(ShowAppreciation));
        Hint = value?.Value switch
        {
            AcademicDocumentType.Certificate => "Atteste l'inscription de l'élève pour l'année scolaire en cours, avec les cours suivis.",
            AcademicDocumentType.Attendance => "Nombre de séances suivies sur la période choisie et taux de présence.",
            _ => IsGroupMode
                ? "Une page par élève : notes, moyennes, rang dans chaque groupe, assiduité."
                : "Notes, moyennes, mention, rang dans chaque groupe et assiduité sur la période.",
        };
    }

    public async Task InitializeForStudentAsync(int studentId, AcademicDocumentType type = AcademicDocumentType.ReportCard)
    {
        _studentId = studentId;
        _groupId = null;
        var s = await students.GetAsync(studentId) ?? throw new BusinessException("Élève introuvable.");
        SubjectLabel = "Élève";
        SubjectName = $"{s.FullName} · {s.Matricule}";
        await InitializeCommonAsync(type);
    }

    public async Task InitializeForGroupAsync(int groupId)
    {
        _groupId = groupId;
        _studentId = null;
        var g = await groups.GetAsync(groupId) ?? throw new BusinessException("Groupe introuvable.");
        SubjectLabel = "Groupe";
        SubjectName = g.FullName;
        await InitializeCommonAsync(AcademicDocumentType.ReportCard);
    }

    private async Task InitializeCommonAsync(AcademicDocumentType type)
    {
        _settings = await settings.GetAsync();
        var today = clock.GetLocalNow().Date;
        var year = DocumentPeriod.AcademicYear(_settings.AcademicYear, today);
        Periods = [new Option<DateTime?>(null, year.Label), .. Options.Months(today).Select(m => new Option<DateTime?>(m.Value, m.Label))];
        SelectedPeriod = Periods.FirstOrDefault(p => p.Value is { } v && v.Year == today.Year && v.Month == today.Month) ?? Periods[0];
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsGroupMode));
        OnPropertyChanged(nameof(ShowTypes));
        OnPropertyChanged(nameof(AppreciationLabel));
        SelectedType = Types.First(t => t.Value == (IsGroupMode ? AcademicDocumentType.ReportCard : type));
    }

    /// <summary>The period currently chosen in the dialog.</summary>
    public DocumentPeriod CurrentPeriod => SelectedPeriod?.Value is { } month
        ? DocumentPeriod.Month(month)
        : DocumentPeriod.AcademicYear(_settings.AcademicYear, clock.GetLocalNow().Date);

    /// <summary>Builds the pages to print with the current choices.</summary>
    public async Task<(string Job, IReadOnlyList<PrintPage> Pages)> BuildPagesAsync()
    {
        var today = clock.GetLocalNow().Date;
        var period = CurrentPeriod;
        var appreciation = string.IsNullOrWhiteSpace(Appreciation) ? null : Appreciation.Trim();
        if (_groupId is { } gid)
        {
            var cards = await documents.GroupReportCardsAsync(gid, period);
            if (cards.Count == 0) throw new BusinessException("Aucun élève inscrit dans ce groupe pendant cette période.");
            return ($"Bulletins · {SubjectName} · {period.Label}", cards.Select(c => AcademicDocumentPages.ReportCard(c, _settings, appreciation, today)).ToList());
        }
        var sid = _studentId ?? throw new BusinessException("Aucun élève sélectionné.");
        return SelectedType?.Value switch
        {
            AcademicDocumentType.Certificate =>
                ($"Certificat de scolarité · {SubjectName}", [AcademicDocumentPages.Certificate(await documents.CertificateAsync(sid), _settings)]),
            AcademicDocumentType.Attendance =>
                ($"Attestation de présence · {SubjectName}", [AcademicDocumentPages.AttendanceCertificate(await documents.AttendanceCertificateAsync(sid, period), _settings)]),
            _ => ($"Bulletin · {SubjectName} · {period.Label}", [AcademicDocumentPages.ReportCard(await documents.ReportCardAsync(sid, period), _settings, appreciation, today)]),
        };
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        var (job, pages) = await BuildPagesAsync();
        var logo = _settings.LogoFile is null ? null : storage.GetPath(_settings.LogoFile, StorageAreas.Images);
        printer.PrintPages(job, _settings, logo, pages);
        PrintedPageCount = pages.Count;
        return true;
    }
}
