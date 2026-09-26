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

public sealed record DocumentLine(int Id, string Title, string FileName, string Category, string OwnerType, string Owner, bool HasOwnerPage,
    string Date, string Size, IRelayCommand OpenOwner, IRelayCommand Open, IRelayCommand Delete);

public sealed partial class DocumentsViewModel(
    IDocumentService documents, IStudentService students, ITeacherService teachers, IParentService parents, IShell shell,
    INavigator nav, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    private List<(Document Doc, string Owner)> _all = [];

    public override string NavKey => "documents";
    public override string Title => "Documents";

    public IReadOnlyList<Option<DocumentOwnerType?>> Filters { get; } =
    [
        new(null, "Tous"), new(DocumentOwnerType.Center, "Centre"), new(DocumentOwnerType.Student, "Élèves"),
        new(DocumentOwnerType.Teacher, "Enseignants"), new(DocumentOwnerType.Parent, "Parents"),
    ];

    [ObservableProperty] private Option<DocumentOwnerType?>? _selectedFilter;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<DocumentLine> _rows = [];
    [ObservableProperty] private string _countLabel = "";

    partial void OnSelectedFilterChanged(Option<DocumentOwnerType?>? value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public override async Task LoadAsync(object? parameter)
    {
        SelectedFilter ??= Filters[0];
        await RunAsync(async () =>
        {
            var docs = await documents.ListAsync();
            var needStudents = docs.Any(d => d.OwnerType == DocumentOwnerType.Student);
            var needTeachers = docs.Any(d => d.OwnerType == DocumentOwnerType.Teacher);
            var needParents = docs.Any(d => d.OwnerType == DocumentOwnerType.Parent);
            var now = clock.GetLocalNow().DateTime;
            var studentNames = needStudents ? (await students.ListAsync(now)).ToDictionary(s => s.Id, s => s.FullName) : [];
            var teacherNames = needTeachers ? (await teachers.ListAsync(now)).ToDictionary(t => t.Id, t => t.FullName) : [];
            var parentNames = needParents ? (await parents.ListAsync()).ToDictionary(p => p.Id, p => p.FullName) : [];
            _all = docs.Select(d => (d, d.OwnerType switch
            {
                DocumentOwnerType.Center => "Centre",
                DocumentOwnerType.Student => Lookup(studentNames, d.OwnerId, "Élève supprimé"),
                DocumentOwnerType.Teacher => Lookup(teacherNames, d.OwnerId, "Enseignant supprimé"),
                _ => Lookup(parentNames, d.OwnerId, "Parent supprimé"),
            })).ToList();
            ApplyFilter();
        }, notifier);
    }

    private static string Lookup(Dictionary<int, string> names, int? id, string missing) =>
        id is { } i && names.TryGetValue(i, out var n) ? n : missing;

    private void ApplyFilter()
    {
        var q = SearchText.Trim().ToLowerInvariant();
        var type = SelectedFilter?.Value;
        var list = _all
            .Where(x => type is null || x.Doc.OwnerType == type)
            .Where(x => q.Length == 0 || $"{x.Doc.Title} {x.Doc.OriginalName} {x.Doc.Category} {x.Owner}".ToLowerInvariant().Contains(q))
            .ToList();
        Rows = list.Select(x =>
        {
            var d = x.Doc;
            return new DocumentLine(d.Id, d.Title, d.OriginalName, d.Category ?? "—", Labels.Of(d.OwnerType), x.Owner,
                d.OwnerType != DocumentOwnerType.Center && d.OwnerId is not null,
                d.CreatedAt.ToString("dd/MM/yyyy"), StudentDetailViewModel.FormatSize(d.SizeBytes),
                new AsyncRelayCommand(() => OpenOwnerAsync(d)),
                new RelayCommand(() => OpenFile(d)),
                new AsyncRelayCommand(() => DeleteAsync(d)));
        }).ToList();
        CountLabel = $"{list.Count} document{(list.Count > 1 ? "s" : "")}";
    }

    private Task OpenOwnerAsync(Document d) => (d.OwnerType, d.OwnerId) switch
    {
        (DocumentOwnerType.Student, { } id) => nav.NavigateAsync<StudentDetailViewModel>(id),
        (DocumentOwnerType.Teacher, { } id) => nav.NavigateAsync<TeacherDetailViewModel>(id),
        (DocumentOwnerType.Parent, { } id) => nav.NavigateAsync<ParentsViewModel>(id),
        _ => Task.CompletedTask,
    };

    private void OpenFile(Document d)
    {
        var path = documents.GetFullPath(d);
        if (!File.Exists(path))
        {
            notifier.Error("Fichier introuvable dans le dossier des documents.");
            return;
        }
        shell.Open(path);
    }

    private async Task DeleteAsync(Document d)
    {
        if (!await dialogs.ConfirmAsync("Supprimer le document", $"Supprimer « {d.Title} » ? Le fichier sera effacé.")) return;
        if (await RunAsync(() => documents.DeleteAsync(d.Id), notifier))
        {
            notifier.Info("Document supprimé");
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task Add()
    {
        var dialog = services.GetRequiredService<AddDocumentDialogViewModel>();
        dialog.Initialize(DocumentOwnerType.Center, null, "Centre");
        if (!dialog.HasFile) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Document ajouté");
            await RefreshAsync();
        }
    }
}
