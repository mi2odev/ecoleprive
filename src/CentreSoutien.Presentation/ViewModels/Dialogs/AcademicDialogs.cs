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

/// <summary>Create or edit a course (subject × level, monthly price).</summary>
public sealed partial class CourseEditorDialogViewModel(ICourseService courses, ICrudService<Subject> subjects, IStudentService students) : DialogViewModel
{
    private Course _course = new();

    public override string Title => _course.Id == 0 ? "Nouveau cours" : "Modifier le cours";
    public int? SavedId { get; private set; }

    [ObservableProperty] private IReadOnlyList<Option<int>> _subjectOptions = [];
    [ObservableProperty] private Option<int>? _selectedSubject;
    [ObservableProperty] private IReadOnlyList<string> _levels = Options.DefaultLevels;
    [ObservableProperty] private string _level = "";
    [ObservableProperty] private string _price = "";
    [ObservableProperty] private string? _description;
    [ObservableProperty] private bool _isActive = true;

    public async Task InitializeAsync(int? id)
    {
        if (id is { } courseId)
        {
            var list = await courses.ListAsync();
            _course = list.FirstOrDefault(c => c.Id == courseId) ?? throw new BusinessException("Cours introuvable.");
        }
        else _course = new Course();
        OnPropertyChanged(nameof(Title));

        SubjectOptions = (await subjects.ListAsync()).Select(s => new Option<int>(s.Id, s.Name)).ToList();
        SelectedSubject = SubjectOptions.FirstOrDefault(s => s.Value == _course.SubjectId) ?? (_course.Id == 0 ? SubjectOptions.FirstOrDefault() : null);
        var levels = await students.LevelsAsync();
        Levels = Options.DefaultLevels.Union(levels).Where(l => !string.IsNullOrWhiteSpace(l)).OrderBy(Domain.Calculations.Levels.Order).ToList();
        Level = _course.Level;
        Price = _course.Id == 0 ? "" : Money.Number(_course.MonthlyPrice);
        Description = _course.Description;
        IsActive = _course.IsActive;
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (SelectedSubject is null) throw new BusinessException("Choisissez une matière.");
        if (string.IsNullOrWhiteSpace(Level)) throw new BusinessException("Le niveau est obligatoire.");
        var price = Parse.Amount(Price) ?? throw new BusinessException("Saisissez un prix mensuel valide.");
        _course.SubjectId = SelectedSubject.Value;
        _course.Level = Level.Trim().ToUpperInvariant();
        _course.MonthlyPrice = price;
        _course.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        _course.IsActive = IsActive;
        var saved = await courses.SaveAsync(_course);
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

/// <summary>Create or edit a group: course, teacher, room, capacity and weekly timetable, with live conflict detection.</summary>
public sealed partial class GroupEditorDialogViewModel(
    IGroupService groups, ICourseService courses, ITeacherService teachers, ICrudService<Room> rooms, TimeProvider clock) : DialogViewModel
{
    private Group _group = new();
    private List<Course> _courses = [];
    private bool _initializing;
    private int _checkVersion;

    public override string Title => _group.Id == 0 ? "Nouveau groupe" : "Modifier le groupe";
    public override double Width => 640;
    public int? SavedId { get; private set; }

    [ObservableProperty] private IReadOnlyList<Option<int>> _courseOptions = [];
    [ObservableProperty] private Option<int>? _selectedCourse;
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

    public async Task InitializeAsync(int? groupId, int? courseId = null)
    {
        _initializing = true;
        try
        {
            _group = groupId is { } id ? await groups.GetAsync(id) ?? throw new BusinessException("Groupe introuvable.") : new Group();
            OnPropertyChanged(nameof(Title));

            _courses = await courses.ListAsync();
            CourseOptions = _courses.Where(c => c.IsActive || c.Id == _group.CourseId || c.Id == courseId)
                .Select(c => new Option<int>(c.Id, c.Name)).ToList();
            var cid = _group.Id != 0 ? _group.CourseId : courseId;
            SelectedCourse = CourseOptions.FirstOrDefault(c => c.Value == cid) ?? (_group.Id == 0 ? CourseOptions.FirstOrDefault() : null);

            var period = clock.GetLocalNow().DateTime;
            TeacherOptions = [new Option<int?>(null, "Aucun"), .. (await teachers.ListAsync(period))
                .Where(t => t.IsActive || t.Id == _group.TeacherId).Select(t => new Option<int?>(t.Id, t.FullName))];
            SelectedTeacher = TeacherOptions.FirstOrDefault(t => t.Value == _group.TeacherId) ?? TeacherOptions[0];

            var roomList = (await rooms.ListAsync()).Where(r => r.IsActive || r.Id == _group.RoomId || _group.Slots.Any(s => s.RoomId == r.Id)).ToList();
            RoomOptions = [new Option<int?>(null, "Aucune"), .. roomList.Select(r => new Option<int?>(r.Id, $"{r.Name} · {r.Capacity} places"))];
            SelectedRoom = RoomOptions.FirstOrDefault(r => r.Value == _group.RoomId) ?? RoomOptions[0];
            _slotRooms = [new Option<int?>(null, "Salle du groupe"), .. roomList.Select(r => new Option<int?>(r.Id, r.Name))];

            Name = _group.Id != 0 ? _group.Name : NextName(SelectedCourse?.Value);
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

    private string NextName(int? courseId)
    {
        var used = _courses.FirstOrDefault(c => c.Id == courseId)?.Groups.Select(g => g.Name).ToHashSet() ?? [];
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

    partial void OnSelectedCourseChanged(Option<int>? value)
    {
        if (_initializing || _group.Id != 0) return;
        Name = NextName(value?.Value);
    }

    partial void OnSelectedTeacherChanged(Option<int?>? value) => ScheduleCheck();
    partial void OnSelectedRoomChanged(Option<int?>? value) => ScheduleCheck();

    partial void OnConflictsChanged(IReadOnlyList<string> value) => HasConflicts = value.Count > 0;

    private Group Draft() => new()
    {
        Id = _group.Id,
        CourseId = SelectedCourse?.Value ?? 0,
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
        if (SelectedCourse is null) throw new BusinessException("Choisissez un cours.");
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
