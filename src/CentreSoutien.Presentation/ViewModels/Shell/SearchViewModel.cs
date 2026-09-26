using CentreSoutien.Application.Abstractions;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Shell;

/// <summary>One result line of the search palette.</summary>
public sealed partial class SearchResultItem(SearchHit hit, string category, Func<SearchResultItem, Task> open) : ObservableObject
{
    public SearchHit Hit => hit;
    public string Category => category;
    public string Title => hit.Title;
    public string? Subtitle => hit.Subtitle;
    public bool HasSubtitle => !string.IsNullOrEmpty(hit.Subtitle);

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private Task Open() => open(this);
}

public sealed class SearchResultGroup(string label, IReadOnlyList<SearchResultItem> items)
{
    public string Label => label;
    /// <summary>Uppercase caption as in the sidebar (WPF has no text-transform).</summary>
    public string Caption => label.ToUpperInvariant();
    public IReadOnlyList<SearchResultItem> Items => items;
}

/// <summary>
/// Global search palette (Ctrl+K): debounced query, results grouped by category, keyboard selection,
/// and navigation to the matching page.
/// </summary>
public sealed partial class SearchViewModel(ISearchService search, INavigator nav, INotifier notifier) : ViewModelBase
{
    private CancellationTokenSource? _debounce;
    private int _version;
    private List<SearchResultItem> _flat = [];

    /// <summary>Delay between the last keystroke and the search.</summary>
    public TimeSpan DebounceDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>The search in progress (or the last one); tests await it.</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHint))]
    private string _query = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults), nameof(ShowNoResult))]
    private IReadOnlyList<SearchResultGroup> _groups = [];

    [ObservableProperty]
    private SearchResultItem? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResult))]
    private bool _hasSearched;

    public IReadOnlyList<SearchResultItem> Results => _flat;
    public bool HasResults => _flat.Count > 0;
    public bool ShowHint => string.IsNullOrWhiteSpace(Query);
    public bool ShowNoResult => HasSearched && !HasResults && !string.IsNullOrWhiteSpace(Query);

    partial void OnQueryChanged(string value)
    {
        // Hide the previous "no result" message until the new query has been searched.
        HasSearched = false;
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        Pending = DebouncedSearchAsync(value, cts.Token);
    }

    partial void OnSelectedChanged(SearchResultItem? oldValue, SearchResultItem? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    private async Task DebouncedSearchAsync(string text, CancellationToken ct)
    {
        try
        {
            if (DebounceDelay > TimeSpan.Zero) await Task.Delay(DebounceDelay, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        await SearchNowAsync(text);
    }

    /// <summary>Runs the search immediately (no debounce).</summary>
    public async Task SearchNowAsync(string? text = null)
    {
        text ??= Query;
        var version = ++_version;
        if (string.IsNullOrWhiteSpace(text))
        {
            Apply([]);
            HasSearched = false;
            return;
        }
        List<SearchHit> hits = [];
        await RunAsync(async () => hits = await search.SearchAsync(text), notifier);
        if (version != _version) return; // a newer query has been typed meanwhile
        Apply(hits);
        HasSearched = true;
    }

    private void Apply(List<SearchHit> hits)
    {
        var groups = hits.GroupBy(h => h.Category)
            .OrderBy(g => g.Key)
            .Select(g => new SearchResultGroup(CategoryLabel(g.Key), g.Select(h => new SearchResultItem(h, CategoryLabel(h.Category), OpenAsync)).ToList()))
            .ToList();
        _flat = groups.SelectMany(g => g.Items).ToList();
        Groups = groups;
        OnPropertyChanged(nameof(Results));
        Selected = _flat.FirstOrDefault();
    }

    public static string CategoryLabel(SearchCategory c) => c switch
    {
        SearchCategory.Student => "Élèves",
        SearchCategory.Parent => "Parents",
        SearchCategory.Teacher => "Enseignants",
        SearchCategory.Group => "Groupes",
        SearchCategory.Document => "Documents",
        _ => "Reçus",
    };

    /// <summary>Shows the palette, optionally pre-filled (text typed in the header box).</summary>
    public void Open(string? text = null)
    {
        IsOpen = true;
        if (text is not null && text != Query) Query = text;
    }

    [RelayCommand]
    public void Close()
    {
        _debounce?.Cancel();
        _version++;
        IsOpen = false;
        Query = "";
        _debounce?.Cancel(); // Query = "" scheduled a search: nothing to look for
        Apply([]);
        HasSearched = false;
    }

    [RelayCommand]
    private void MoveDown() => Move(+1);

    [RelayCommand]
    private void MoveUp() => Move(-1);

    private void Move(int delta)
    {
        if (_flat.Count == 0) return;
        var i = Selected is null ? -1 : _flat.IndexOf(Selected);
        i = i < 0 ? (delta > 0 ? 0 : _flat.Count - 1) : (i + delta + _flat.Count) % _flat.Count;
        Selected = _flat[i];
    }

    /// <summary>Enter: opens the selected result (after finishing a search still waiting for the debounce).</summary>
    [RelayCommand]
    private async Task Activate()
    {
        if (!Pending.IsCompleted)
        {
            _debounce?.Cancel();
            await SearchNowAsync();
        }
        if (Selected is { } item) await OpenAsync(item);
    }

    private async Task OpenAsync(SearchResultItem item)
    {
        var hit = item.Hit;
        Close();
        try
        {
            switch (hit.Category)
            {
                case SearchCategory.Student:
                    await nav.NavigateAsync<StudentDetailViewModel>(hit.Id);
                    break;
                case SearchCategory.Parent:
                    await nav.NavigateAsync<ParentsViewModel>(hit.Id);
                    break;
                case SearchCategory.Teacher:
                    await nav.NavigateAsync<TeacherDetailViewModel>(hit.Id);
                    break;
                case SearchCategory.Group:
                    await nav.NavigateAsync<GroupDetailViewModel>(new GroupDetailViewModel.Target(hit.Id));
                    break;
                case SearchCategory.Document:
                    await nav.NavigateAsync<DocumentsViewModel>();
                    break;
                case SearchCategory.Receipt when hit.RelatedId is { } studentId:
                    await nav.NavigateAsync<StudentDetailViewModel>(studentId);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorLog.Write(ex);
            notifier.Error("Impossible d'ouvrir ce résultat : " + ex.Message);
        }
    }
}
