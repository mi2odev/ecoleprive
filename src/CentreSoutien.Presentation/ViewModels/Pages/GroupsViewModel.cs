using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

public sealed record GroupRow(Group Group, int Enrolled, IRelayCommand Open, IRelayCommand Edit, IRelayCommand Delete)
{
    public int Id => Group.Id;
    public string Name => Group.FullName;
    public string Teacher => Group.Teacher?.FullName ?? "—";
    public string Room => Group.Room?.Name ?? "—";
    public string Schedule => Group.Slots.Count == 0 ? "Non planifié" : Labels.Slots(Group.Slots);
    public string Fill => $"{Enrolled} / {Group.Capacity}";
    public bool IsFull => Enrolled >= Group.Capacity;
    public Badge FullBadge => new(Fill, BadgeKind.Warn);
    public Badge Status => Badge.Active(Group.IsActive && Group.Course?.IsActive != false);
}

public sealed partial class GroupsViewModel(
    IGroupService groups, INavigator nav, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel, IHasPrimaryAction
{
    private List<Group> _all = [];

    public override string NavKey => "groups";
    public override string Title => "Groupes";

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<string> _levels = ["Tous"];
    [ObservableProperty] private string _selectedLevel = "Tous";
    [ObservableProperty] private IReadOnlyList<GroupRow> _rows = [];
    [ObservableProperty] private string _countLabel = "";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLevelChanged(string value) => ApplyFilter();

    public override async Task LoadAsync(object? parameter)
    {
        await RunAsync(async () =>
        {
            _all = await groups.ListAsync();
            Levels = ["Tous", .. _all.Select(g => g.Course!.Level).Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(Domain.Calculations.Levels.Order)];
            if (!Levels.Contains(SelectedLevel)) SelectedLevel = "Tous";
            ApplyFilter();
        }, notifier);
    }

    private void ApplyFilter()
    {
        var today = clock.GetLocalNow().Date;
        var q = SearchText.Trim().ToLowerInvariant();
        var list = _all
            .Where(g => SelectedLevel == "Tous" || g.Course!.Level == SelectedLevel)
            .Where(g => q.Length == 0 || $"{g.FullName} {g.Teacher?.FullName} {g.Room?.Name}".ToLowerInvariant().Contains(q))
            .ToList();
        Rows = list.Select(g => new GroupRow(g, g.Enrollments.Count(e => e.IsActiveOn(today)),
            new AsyncRelayCommand(() => nav.NavigateAsync<CourseDetailViewModel>(new CourseDetailViewModel.Target(g.CourseId, g.Id))),
            new AsyncRelayCommand(() => EditAsync(g.Id, null)),
            new AsyncRelayCommand(() => DeleteAsync(g)))).ToList();
        CountLabel = $"{list.Count} groupe{(list.Count > 1 ? "s" : "")} affiché{(list.Count > 1 ? "s" : "")} sur {_all.Count}";
    }

    [RelayCommand]
    private Task Add() => EditAsync(null, null);

    private async Task EditAsync(int? id, int? courseId)
    {
        var dialog = services.GetRequiredService<GroupEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(id, courseId), notifier)) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info(id is null ? "Groupe créé" : "Groupe enregistré");
            await RefreshAsync();
        }
    }

    private async Task DeleteAsync(Group g)
    {
        if (!await dialogs.ConfirmAsync("Supprimer le groupe", $"Supprimer {g.FullName} et son emploi du temps ?")) return;
        if (await RunAsync(() => groups.DeleteAsync(g.Id), notifier))
        {
            notifier.Info("Groupe supprimé");
            await RefreshAsync();
        }
    }

    // Ctrl+N in the shell.
    IAsyncRelayCommand? IHasPrimaryAction.PrimaryCommand => AddCommand;
    string? IHasPrimaryAction.PrimaryLabel => "Nouveau groupe";
}
