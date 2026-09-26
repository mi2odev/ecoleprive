using CentreSoutien.Application.Abstractions;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Dialogs;

/// <summary>One previewed spreadsheet row, as displayed in the import dialog.</summary>
public sealed record ImportPreviewLine(ImportRow Row, bool Skipped)
{
    public int Line => Row.Data.RowNumber;
    public string Name => Row.Data.FullName.Length == 0 ? "—" : Row.Data.FullName;
    public string Level => Row.Data.Level.Length == 0 ? "—" : Row.Data.Level;
    public string BirthDate => Row.Data.BirthDate?.ToString("dd/MM/yyyy") ?? Row.Data.BirthDateText ?? "—";
    public string Parent => Row.ParentAction ?? "—";
    public string Groups => Row.MatchedGroups.Count == 0 ? "—" : string.Join(", ", Row.MatchedGroups);
    public string Messages => string.Join("\n", Row.Messages);
    public bool HasMessages => Row.Messages.Count > 0;

    public Badge Status => Row.Status switch
    {
        ImportRowStatus.Error => new Badge("Erreur", BadgeKind.Bad),
        _ when Skipped => new Badge("Ignorée", BadgeKind.Neutral),
        ImportRowStatus.Warning => new Badge("Avertissement", BadgeKind.Warn),
        _ => new Badge("OK", BadgeKind.Ok),
    };
}

/// <summary>Import students from Excel: download the template, choose a file, check the preview, import.</summary>
public sealed partial class ImportStudentsDialogViewModel(IImportService import, IFilePicker files, IShell shell, INotifier notifier) : DialogViewModel
{
    private const string ExcelFilter = "Classeur Excel|*.xlsx";

    public override string Title => "Importer des élèves depuis Excel";
    public override double Width => 900;
    public override string ConfirmText => Preview is null ? "Importer" : ToImport == 1 ? "Importer 1 élève" : $"Importer {ToImport} élèves";

    /// <summary>Set after a successful import.</summary>
    public ImportResult? Result { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(ConfirmText), nameof(FileName))]
    private ImportPreview? _preview;

    [ObservableProperty] private bool _skipDuplicates = true;
    [ObservableProperty] private IReadOnlyList<ImportPreviewLine> _lines = [];
    [ObservableProperty] private string _summary = "";

    public bool HasPreview => Preview is not null;
    public string FileName => Preview is null ? "Aucun fichier choisi" : Path.GetFileName(Preview.Path);
    public int ToImport => Preview?.ImportableCount(SkipDuplicates) ?? 0;
    public int OkCount => Preview?.OkCount ?? 0;
    public int WarningCount => Preview?.WarningCount ?? 0;
    public int ErrorCount => Preview?.ErrorCount ?? 0;
    public int DuplicateCount => Preview?.DuplicateCount ?? 0;

    partial void OnPreviewChanged(ImportPreview? value) => Rebuild();
    partial void OnSkipDuplicatesChanged(bool value) => Rebuild();

    private void Rebuild()
    {
        var p = Preview;
        Lines = p is null ? [] : p.Rows.Select(r => new ImportPreviewLine(r, r.Status != ImportRowStatus.Error && r.IsDuplicate && SkipDuplicates)).ToList();
        Summary = p is null ? "" : $"{p.Rows.Count} ligne{(p.Rows.Count > 1 ? "s" : "")} lue{(p.Rows.Count > 1 ? "s" : "")} · {ToImport} à importer · " +
            $"{p.ErrorCount} en erreur{(DuplicateCount > 0 ? $" · {DuplicateCount} doublon{(DuplicateCount > 1 ? "s" : "")}" : "")}";
        OnPropertyChanged(nameof(ToImport));
        OnPropertyChanged(nameof(OkCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(DuplicateCount));
        OnPropertyChanged(nameof(ConfirmText));
    }

    [RelayCommand]
    private async Task DownloadTemplate()
    {
        var path = files.SaveFile("Enregistrer le modèle d'import", "modele-import-eleves.xlsx", ExcelFilter);
        if (path is null) return;
        await RunAsync(async () =>
        {
            await import.CreateTemplateAsync(path);
            notifier.Info("Modèle Excel enregistré");
            shell.Reveal(path);
        });
    }

    [RelayCommand]
    private async Task PickFile()
    {
        var path = files.OpenFile("Choisir le fichier des élèves", ExcelFilter);
        if (path is null) return;
        await LoadFileAsync(path);
    }

    /// <summary>Reads and checks the workbook (also used to reload after editing it in Excel).</summary>
    public async Task LoadFileAsync(string path)
    {
        var ok = await RunAsync(async () => Preview = await import.PreviewAsync(path));
        if (!ok) Preview = null;
    }

    [RelayCommand]
    private Task Reload() => Preview is null ? Task.CompletedTask : LoadFileAsync(Preview.Path);

    protected override async Task<bool> OnConfirmAsync()
    {
        if (Preview is null) throw new BusinessException("Choisissez d'abord le fichier Excel à importer.");
        if (ToImport == 0) throw new BusinessException("Aucune ligne à importer : corrigez les erreurs dans le fichier puis rechargez-le.");
        Result = await import.ImportAsync(Preview, new ImportOptions(SkipDuplicates));
        return true;
    }
}
