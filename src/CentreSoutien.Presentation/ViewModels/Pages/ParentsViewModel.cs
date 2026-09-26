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

/// <summary>Entry of the parents list (left column). <see cref="IsSelected"/> highlights the parent shown on the right.</summary>
public sealed partial class ParentListRow(Parent parent, decimal balance, IRelayCommand select) : ObservableObject
{
    public int Id => parent.Id;
    public string Name => parent.FullName;
    public string Relation => string.IsNullOrWhiteSpace(parent.Relation) ? "—" : parent.Relation;
    public string Phone => parent.Phone ?? "—";
    public string Children => parent.Children.Count == 0 ? "Aucun enfant lié" : string.Join(", ", parent.Children.OrderBy(c => c.FirstName).Select(c => c.FirstName));
    public int ChildCount => parent.Children.Count;
    public string ChildCountLabel => ChildCount == 0 ? "—" : $"{ChildCount} enfant{(ChildCount > 1 ? "s" : "")}";
    public decimal BalanceValue => balance;
    public string Balance => balance > 0 ? Money.Format(balance) : "—";
    public IRelayCommand Select => select;

    [ObservableProperty] private bool _isSelected;
}

public sealed record ParentChildRow(int Id, string Name, string Initials, string Level, string Details, string Balance, Badge State, IRelayCommand Open);

public sealed partial class ParentsViewModel(
    IParentService parents, IStudentService students, IDocumentService documents, INavigator nav, DialogHost dialogs, INotifier notifier,
    IFilePicker files, IExportService export, IShell shell, TimeProvider clock, IServiceProvider services) : PageViewModel, IHasPrimaryAction
{
    private List<Parent> _all = [];
    private Dictionary<int, decimal> _balances = [];
    private Parent? _selected;

    public override string NavKey => "parents";
    public override string Title => "Parents";

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<ParentListRow> _rows = [];
    [ObservableProperty] private string _countLabel = "";
    [ObservableProperty] private string _monthLabel = "";

    // Selected parent (right column).
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _selectedName = "";
    [ObservableProperty] private string _selectedSubtitle = "";
    [ObservableProperty] private IReadOnlyList<Field> _info = [];
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string _childrenTitle = "Enfants";
    [ObservableProperty] private IReadOnlyList<ParentChildRow> _children = [];
    [ObservableProperty] private string _balanceLabel = "";
    [ObservableProperty] private IReadOnlyList<DocumentRow> _documents = [];

    public int? SelectedId => _selected?.Id;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public override async Task LoadAsync(object? parameter)
    {
        var wanted = parameter as int? ?? _selected?.Id;
        await RunAsync(async () =>
        {
            var now = clock.GetLocalNow().DateTime;
            MonthLabel = Labels.Month(Period.Of(now));
            _all = await parents.ListAsync();
            _balances = (await students.ListAsync(now)).ToDictionary(s => s.Id, s => s.Balance);
            if (parameter is int) SearchText = "";
            ApplyFilter();
            var id = wanted is { } w && _all.Any(p => p.Id == w) ? w : Rows.FirstOrDefault()?.Id;
            await SelectAsync(id);
        }, notifier);
    }

    private decimal BalanceOf(Parent p) => p.Children.Sum(c => _balances.GetValueOrDefault(c.Id));

    private void ApplyFilter()
    {
        var q = SearchText.Trim().ToLowerInvariant();
        var list = _all
            .Where(p => q.Length == 0 || $"{p.FullName} {p.Phone} {p.Phone2} {string.Join(" ", p.Children.Select(c => c.FullName))}".ToLowerInvariant().Contains(q))
            .ToList();
        Rows = list.Select(p =>
        {
            var row = new ParentListRow(p, BalanceOf(p), new AsyncRelayCommand(() => SelectAsync(p.Id)));
            row.IsSelected = p.Id == _selected?.Id;
            return row;
        }).ToList();
        CountLabel = $"{list.Count} parent{(list.Count > 1 ? "s" : "")} affiché{(list.Count > 1 ? "s" : "")} sur {_all.Count}";
    }

    private async Task SelectAsync(int? id)
    {
        foreach (var r in Rows) r.IsSelected = r.Id == id;
        if (id is null)
        {
            _selected = null;
            HasSelection = false;
            OnPropertyChanged(nameof(SelectedId));
            return;
        }
        await RunAsync(async () =>
        {
            var p = await parents.GetAsync(id.Value) ?? throw new BusinessException("Parent introuvable.");
            _selected = p;
            OnPropertyChanged(nameof(SelectedId));
            HasSelection = true;
            var period = Period.Of(clock.GetLocalNow().DateTime);
            SelectedName = p.FullName;
            var n = p.Children.Count;
            SelectedSubtitle = string.Join(" · ", new[] { p.Relation, n == 0 ? "Aucun enfant lié" : $"{n} enfant{(n > 1 ? "s" : "")}" }.Where(x => !string.IsNullOrWhiteSpace(x)));
            Info =
            [
                new("Téléphone", p.Phone ?? "—"),
                new("Téléphone 2", p.Phone2 ?? "—"),
                new("E-mail", p.Email ?? "—"),
                new("Profession", p.Profession ?? "—"),
                new("Adresse", p.Address ?? "—"),
                new("Lien", p.Relation ?? "—"),
            ];
            Notes = string.IsNullOrWhiteSpace(p.Notes) ? null : p.Notes;
            ChildrenTitle = $"Enfants ({n})";
            Children = p.Children.OrderBy(c => c.FirstName).Select(c =>
            {
                var courses = string.Join(", ", c.Enrollments.Where(e => e.CoversMonth(period.Year, period.Month)).Select(e => e.Group?.Course?.Subject?.Display).Where(x => x is not null).Distinct());
                var balance = Billing.Balance(c, period);
                return new ParentChildRow(c.Id, c.FullName, c.Initials, string.IsNullOrWhiteSpace(c.Level) ? "—" : c.Level,
                    string.Join(" · ", new[] { c.Matricule, courses }.Where(x => !string.IsNullOrWhiteSpace(x))),
                    balance > 0 ? "Reste " + Money.Format(balance) : "", Badge.For(Billing.State(c, period)),
                    new AsyncRelayCommand(() => nav.NavigateAsync<StudentDetailViewModel>(c.Id)));
            }).ToList();
            var total = p.Children.Sum(c => Billing.Balance(c, period));
            BalanceLabel = total > 0 ? $"Reste à payer ({Labels.Month(period)}) : {Money.Format(total)}" : $"Aucun impayé pour {Labels.Month(period).ToLowerInvariant()}";
            await LoadDocumentsAsync();
        }, notifier);
    }

    private async Task LoadDocumentsAsync()
    {
        if (_selected is null) return;
        var docs = await documents.ListAsync(DocumentOwnerType.Parent, _selected.Id);
        Documents = docs.Select(d => new DocumentRow(d.Title, d.Category ?? "—", d.CreatedAt.ToString("dd/MM/yyyy"), StudentDetailViewModel.FormatSize(d.SizeBytes),
            new RelayCommand(() => shell.Open(documents.GetFullPath(d))),
            new AsyncRelayCommand(async () =>
            {
                if (!await dialogs.ConfirmAsync("Supprimer le document", $"Supprimer « {d.Title} » ? Le fichier sera effacé.")) return;
                await RunAsync(async () => { await documents.DeleteAsync(d.Id); await LoadDocumentsAsync(); }, notifier);
            }))).ToList();
    }

    [RelayCommand]
    private async Task Add()
    {
        var dialog = services.GetRequiredService<ParentEditorDialogViewModel>();
        await dialog.InitializeAsync(null);
        if (await dialogs.ShowAsync(dialog) && dialog.SavedId is { } id)
        {
            notifier.Info("Parent ajouté");
            SearchText = "";
            await LoadAsync(id);
        }
    }

    [RelayCommand]
    private async Task Edit()
    {
        if (_selected is null) return;
        var dialog = services.GetRequiredService<ParentEditorDialogViewModel>();
        await dialog.InitializeAsync(_selected.Id);
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Fiche parent enregistrée");
            await LoadAsync(_selected.Id);
        }
    }

    [RelayCommand]
    private async Task AddChild()
    {
        if (_selected is null) return;
        var parent = _selected;
        var dialog = services.GetRequiredService<StudentEditorDialogViewModel>();
        await dialog.InitializeAsync(null);
        dialog.SelectedParent = dialog.Parents.FirstOrDefault(o => o.Value == parent.Id) ?? dialog.SelectedParent;
        dialog.Address ??= parent.Address;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Enfant ajouté");
            await LoadAsync(parent.Id);
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (_selected is null) return;
        var p = _selected;
        var n = p.Children.Count;
        var kids = n == 0 ? "" : $" Ses {n} enfant{(n > 1 ? "s" : "")} ser{(n > 1 ? "ont" : "a")} conservé{(n > 1 ? "s" : "")} sans parent lié.";
        if (!await dialogs.ConfirmAsync("Supprimer le parent", $"Supprimer {p.FullName} ?{kids}")) return;
        if (await RunAsync(() => parents.DeleteAsync(p.Id), notifier))
        {
            notifier.Info("Parent supprimé");
            _selected = null;
            await LoadAsync(null);
        }
    }

    [RelayCommand]
    private async Task AddDocument()
    {
        if (_selected is null) return;
        var dialog = services.GetRequiredService<AddDocumentDialogViewModel>();
        dialog.Initialize(DocumentOwnerType.Parent, _selected.Id, _selected.FullName);
        if (!dialog.HasFile) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Document ajouté");
            await LoadDocumentsAsync();
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        var path = files.SaveFile("Exporter les parents", $"parents-{clock.GetLocalNow():yyyy-MM-dd}.xlsx", "Classeur Excel|*.xlsx");
        if (path is null) return;
        var ids = Rows.Select(r => r.Id).ToHashSet();
        await RunAsync(async () =>
        {
            await export.ExportTableAsync(path, "Parents",
                ["Nom", "Lien", "Téléphone", "Téléphone 2", "E-mail", "Adresse", "Profession", "Enfants", "Nombre d'enfants", "Reste à payer"],
                _all.Where(p => ids.Contains(p.Id)).Select(p => (IReadOnlyList<object?>)[p.FullName, p.Relation, p.Phone, p.Phone2, p.Email, p.Address, p.Profession,
                    string.Join(", ", p.Children.Select(c => c.FullName)), p.Children.Count, BalanceOf(p)]));
            notifier.Info("Export Excel généré");
            shell.Reveal(path);
        }, notifier);
    }

    // Ctrl+N in the shell.
    IAsyncRelayCommand? IHasPrimaryAction.PrimaryCommand => AddCommand;
    string? IHasPrimaryAction.PrimaryLabel => "Ajouter un parent";
}
