using System.Collections.ObjectModel;
using System.ComponentModel;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Dialogs;

/// <summary>Create or edit a subject (name, short name, description).</summary>
public sealed partial class SubjectEditorDialogViewModel(ICrudService<Subject> subjects) : DialogViewModel
{
    private Subject _subject = new();

    public override string Title => _subject.Id == 0 ? "Nouvelle matière" : "Modifier la matière";
    public int? SavedId { get; private set; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _shortName;
    [ObservableProperty] private string? _description;

    public async Task InitializeAsync(int? id)
    {
        _subject = id is null ? new Subject() : await subjects.GetAsync(id.Value) ?? throw new BusinessException("Matière introuvable.");
        OnPropertyChanged(nameof(Title));
        Name = _subject.Name;
        ShortName = _subject.ShortName;
        Description = _subject.Description;
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new BusinessException("Le nom de la matière est obligatoire.");
        _subject.Name = Name.Trim();
        _subject.ShortName = string.IsNullOrWhiteSpace(ShortName) ? null : ShortName.Trim();
        _subject.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        var saved = await subjects.SaveAsync(_subject);
        SavedId = saved.Id;
        return true;
    }
}

/// <summary>Create or edit a room (name, capacity, equipment, active).</summary>
public sealed partial class RoomEditorDialogViewModel(ICrudService<Room> rooms) : DialogViewModel
{
    private Room _room = new();

    public override string Title => _room.Id == 0 ? "Nouvelle salle" : "Modifier la salle";
    public int? SavedId { get; private set; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _capacity = "16";
    [ObservableProperty] private string? _equipment;
    [ObservableProperty] private bool _isActive = true;

    public async Task InitializeAsync(int? id)
    {
        _room = id is null ? new Room() : await rooms.GetAsync(id.Value) ?? throw new BusinessException("Salle introuvable.");
        OnPropertyChanged(nameof(Title));
        Name = _room.Name;
        Capacity = _room.Capacity.ToString();
        Equipment = _room.Equipment;
        IsActive = _room.IsActive;
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new BusinessException("Le nom de la salle est obligatoire.");
        if (!int.TryParse(Capacity?.Trim(), out var cap) || cap <= 0) throw new BusinessException("Saisissez une capacité valide (nombre de places).");
        _room.Name = Name.Trim();
        _room.Capacity = cap;
        _room.Equipment = string.IsNullOrWhiteSpace(Equipment) ? null : Equipment.Trim();
        _room.IsActive = IsActive;
        var saved = await rooms.SaveAsync(_room);
        SavedId = saved.Id;
        return true;
    }
}

/// <summary>One editable line of a group's weekly timetable.</summary>
public sealed partial class SlotEditor : ObservableObject
{
    public SlotEditor(IReadOnlyList<Option<int?>> rooms, Action<SlotEditor> remove)
    {
        Rooms = rooms;
        _room = rooms[0];
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public IReadOnlyList<Option<DayOfWeek>> Days => Options.Days;
    public IReadOnlyList<Option<int?>> Rooms { get; }
    public IRelayCommand RemoveCommand { get; }

    [ObservableProperty] private Option<DayOfWeek> _day = Options.Days[0];
    [ObservableProperty] private string _start = "09:00";
    [ObservableProperty] private string _end = "11:00";
    [ObservableProperty] private Option<int?> _room;

    /// <summary>Builds the slot, or null when a time cannot be read.</summary>
    public ScheduleSlot? ToSlot() =>
        Parse.Time(Start) is { } s && Parse.Time(End) is { } e
            ? new ScheduleSlot { Day = Day.Value, Start = s, End = e, RoomId = Room.Value }
            : null;
}

/// <summary>
/// Create or edit a group: subject, level, name, payment (price for every N sessions), teacher, room, capacity and weekly timetable,
/// with live conflict detection.
/// </summary>
public sealed partial class GroupEditorDialogViewModel(
    IGroupService groups, ICrudService<Subject> subjects, IStudentService students, ITeacherService teachers, ICrudService<Room> rooms,
    TimeProvider clock) : DialogViewModel
{
    private Group _group = new();
    private List<Group> _allGroups = [];
    private bool _initializing;
    private int _checkVersion;

    public override string Title => _group.Id == 0 ? "Nouveau groupe" : "Modifier le groupe";
    public override double Width => 640;
    public int? SavedId { get; private set; }

    [ObservableProperty] private IReadOnlyList<Option<int>> _subjectOptions = [];
    [ObservableProperty] private Option<int>? _selectedSubject;
    [ObservableProperty] private IReadOnlyList<string> _levels = Options.DefaultLevels;
    [ObservableProperty] private string _level = "";
    [ObservableProperty] private string _price = "";
    /// <summary>Sessions per payment ("4", "8"…): typed or picked from <see cref="PackSizes"/>.</summary>
    [ObservableProperty] private string _sessionsPerPack = "4";
    [ObservableProperty] private string? _description;

    public IReadOnlyList<string> PackSizes { get; } = ["4", "8", "12"];

    /// <summary>"L'élève paie 4 500 DZD à l'inscription, puis toutes les 4 séances."</summary>
    public string PaymentHint => (Parse.Amount(Price), int.TryParse(SessionsPerPack?.Trim(), out var n) ? n : 0) is ({ } p and > 0, > 0 and var k)
        ? $"L'élève paie {Money.Format(p)} en rejoignant le groupe, puis à nouveau toutes les {k} séance{(k > 1 ? "s" : "")}."
        : "L'élève paie en rejoignant le groupe, puis à nouveau à chaque fin de paquet de séances.";

    partial void OnPriceChanged(string value) => OnPropertyChanged(nameof(PaymentHint));
    partial void OnSessionsPerPackChanged(string value) => OnPropertyChanged(nameof(PaymentHint));
    [ObservableProperty] private string _name = "A";
    [ObservableProperty] private IReadOnlyList<Option<int?>> _teacherOptions = [];
    [ObservableProperty] private Option<int?>? _selectedTeacher;
    [ObservableProperty] private IReadOnlyList<Option<int?>> _roomOptions = [];
    [ObservableProperty] private Option<int?>? _selectedRoom;
    [ObservableProperty] private string _capacity = "12";
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private IReadOnlyList<string> _conflicts = [];
    [ObservableProperty] private bool _hasConflicts;

    /// <summary>Room choices for a slot: first entry = the group's room.</summary>
    private IReadOnlyList<Option<int?>> _slotRooms = [];

    public ObservableCollection<SlotEditor> Slots { get; } = [];

    public bool HasNoSlot => Slots.Count == 0;

    /// <param name="groupId">Group to edit, or null for a new group.</param>
    /// <param name="template">For a new group: copy subject, level, price, teacher and room from this group (e.g. to create group B).</param>
    public async Task InitializeAsync(int? groupId, Group? template = null)
    {
        _initializing = true;
        try
        {
            _group = groupId is { } id ? await groups.GetAsync(id) ?? throw new BusinessException("Groupe introuvable.") : new Group();
            OnPropertyChanged(nameof(Title));

            if (_group.Id == 0 && template is not null)
            {
                _group.SubjectId = template.SubjectId;
                _group.Level = template.Level;
                _group.Price = template.Price;
                _group.SessionsPerPack = template.SessionsPerPack;
                _group.TeacherId = template.TeacherId;
                _group.RoomId = template.RoomId;
                _group.Capacity = template.Capacity;
            }
            _allGroups = await groups.ListAsync();
            SubjectOptions = (await subjects.ListAsync()).Select(s => new Option<int>(s.Id, s.Name)).ToList();
            SelectedSubject = SubjectOptions.FirstOrDefault(s => s.Value == _group.SubjectId) ?? (_group.Id == 0 ? SubjectOptions.FirstOrDefault() : null);
            var levels = await students.LevelsAsync();
            Levels = Options.DefaultLevels.Union(levels).Where(l => !string.IsNullOrWhiteSpace(l)).OrderBy(Domain.Calculations.Levels.Order).ToList();
            Level = _group.Level;
            Price = _group.Id == 0 && template is null ? "" : Money.Number(_group.Price);
            SessionsPerPack = _group.SessionsPerPack.ToString();
            Description = _group.Description;

            var period = clock.GetLocalNow().DateTime;
            TeacherOptions = [new Option<int?>(null, "Aucun"), .. (await teachers.ListAsync(period))
                .Where(t => t.IsActive || t.Id == _group.TeacherId).Select(t => new Option<int?>(t.Id, t.FullName))];
            SelectedTeacher = TeacherOptions.FirstOrDefault(t => t.Value == _group.TeacherId) ?? TeacherOptions[0];

            var roomList = (await rooms.ListAsync()).Where(r => r.IsActive || r.Id == _group.RoomId || _group.Slots.Any(s => s.RoomId == r.Id)).ToList();
            RoomOptions = [new Option<int?>(null, "Aucune"), .. roomList.Select(r => new Option<int?>(r.Id, $"{r.Name} · {r.Capacity} places"))];
            SelectedRoom = RoomOptions.FirstOrDefault(r => r.Value == _group.RoomId) ?? RoomOptions[0];
            _slotRooms = [new Option<int?>(null, "Salle du groupe"), .. roomList.Select(r => new Option<int?>(r.Id, r.Name))];

            Name = _group.Id != 0 ? _group.Name : NextName();
            Capacity = _group.Capacity.ToString();
            IsActive = _group.IsActive;

            Slots.Clear();
            foreach (var s in _group.Slots.OrderBy(s => Array.IndexOf(Labels.WeekOrder, s.Day)).ThenBy(s => s.Start))
            {
                var editor = NewSlot();
                editor.Day = Options.Days.First(d => d.Value == s.Day);
                editor.Start = Labels.Time(s.Start);
                editor.End = Labels.Time(s.End);
                editor.Room = _slotRooms.FirstOrDefault(r => r.Value == s.RoomId) ?? _slotRooms[0];
                Slots.Add(editor);
            }
            OnPropertyChanged(nameof(HasNoSlot));
        }
        finally
        {
            _initializing = false;
        }
    }

    /// <summary>First free letter (A, B, C…) among the groups of the same subject and level.</summary>
    private string NextName()
    {
        var level = (Level ?? "").Trim().ToUpperInvariant();
        var used = _allGroups.Where(g => g.SubjectId == SelectedSubject?.Value && g.Level == level && g.Id != _group.Id)
            .Select(g => g.Name).ToHashSet();
        for (var c = 'A'; c <= 'Z'; c++)
            if (!used.Contains(c.ToString())) return c.ToString();
        return "";
    }

    private SlotEditor NewSlot()
    {
        var editor = new SlotEditor(_slotRooms, RemoveSlot);
        editor.PropertyChanged += OnSlotChanged;
        return editor;
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e) => ScheduleCheck();

    [RelayCommand]
    private void AddSlot()
    {
        var editor = NewSlot();
        // Propose the day after the last slot, same hours.
        if (Slots.LastOrDefault() is { } last)
        {
            var i = Array.IndexOf(Labels.WeekOrder, last.Day.Value);
            editor.Day = Options.Days[(i + 1) % Options.Days.Count];
            editor.Start = last.Start;
            editor.End = last.End;
        }
        Slots.Add(editor);
        OnPropertyChanged(nameof(HasNoSlot));
        ScheduleCheck();
    }

    private void RemoveSlot(SlotEditor editor)
    {
        editor.PropertyChanged -= OnSlotChanged;
        Slots.Remove(editor);
        OnPropertyChanged(nameof(HasNoSlot));
        ScheduleCheck();
    }

    partial void OnSelectedSubjectChanged(Option<int>? value)
    {
        if (_initializing || _group.Id != 0) return;
        Name = NextName();
    }

    partial void OnLevelChanged(string value)
    {
        if (_initializing || _group.Id != 0) return;
        Name = NextName();
    }

    partial void OnSelectedTeacherChanged(Option<int?>? value) => ScheduleCheck();
    partial void OnSelectedRoomChanged(Option<int?>? value) => ScheduleCheck();

    partial void OnConflictsChanged(IReadOnlyList<string> value) => HasConflicts = value.Count > 0;

    private Group Draft() => new()
    {
        Id = _group.Id,
        SubjectId = SelectedSubject?.Value ?? 0,
        Level = (Level ?? "").Trim().ToUpperInvariant(),
        Price = Parse.Amount(Price) ?? 0,
        SessionsPerPack = int.TryParse(SessionsPerPack?.Trim(), out var pack) ? pack : 0,
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        Name = (Name ?? "").Trim(),
        TeacherId = SelectedTeacher?.Value,
        RoomId = SelectedRoom?.Value,
        Capacity = int.TryParse(Capacity?.Trim(), out var cap) ? cap : 0,
        IsActive = IsActive,
    };

    /// <summary>Checks the timetable against the other groups in the background (latest request wins).</summary>
    private async void ScheduleCheck()
    {
        if (_initializing) return;
        var version = ++_checkVersion;
        try
        {
            await Task.Delay(250);
            if (version != _checkVersion) return;
            var result = await CheckAsync();
            if (version == _checkVersion) Conflicts = result;
        }
        catch (Exception)
        {
            // Live check is advisory only; the save re-validates.
        }
    }

    [RelayCommand]
    private async Task CheckConflicts() => Conflicts = await CheckAsync();

    private async Task<List<string>> CheckAsync()
    {
        var errors = new List<string>();
        var slots = new List<ScheduleSlot>();
        foreach (var s in Slots)
        {
            if (s.ToSlot() is { } slot) slots.Add(slot);
            else errors.Add($"{s.Day.Label} : heure invalide (format 09:00).");
        }
        if (slots.Count > 0) errors.AddRange(await groups.FindConflictsAsync(Draft(), slots));
        return errors;
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (SelectedSubject is null) throw new BusinessException("Choisissez une matière.");
        if (string.IsNullOrWhiteSpace(Level)) throw new BusinessException("Le niveau est obligatoire.");
        if (!int.TryParse(SessionsPerPack?.Trim(), out var pack) || pack is < 1 or > 60)
            throw new BusinessException("Indiquez toutes les combien de séances l'élève paie (ex. 4 ou 8).");
        if (Parse.Amount(Price) is not { } price || price < 0) throw new BusinessException("Saisissez un prix valide.");
        if (string.IsNullOrWhiteSpace(Name)) throw new BusinessException("Le nom du groupe est obligatoire.");
        if (!int.TryParse(Capacity?.Trim(), out var cap) || cap <= 0) throw new BusinessException("Saisissez une capacité valide (nombre de places).");
        var slots = new List<ScheduleSlot>();
        foreach (var s in Slots)
            slots.Add(s.ToSlot() ?? throw new BusinessException($"{s.Day.Label} : heure invalide. Utilisez le format 09:00."));

        _checkVersion++; // cancel pending live checks
        var draft = Draft();
        var conflicts = await groups.FindConflictsAsync(draft, slots);
        Conflicts = conflicts;
        if (conflicts.Count > 0) throw new BusinessException("Enregistrement impossible : l'emploi du temps est en conflit (voir ci-dessus).");
        var saved = await groups.SaveAsync(draft, slots);
        SavedId = saved.Id;
        return true;
    }
}
