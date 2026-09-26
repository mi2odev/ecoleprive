using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Dialogs;

/// <summary>Create or edit a student (identity, level, parent, discount, photo).</summary>
public sealed partial class StudentEditorDialogViewModel(
    IStudentService students, IParentService parents, ICrudService<Discount> discounts, IFileStorage storage, IFilePicker files, TimeProvider clock) : DialogViewModel
{
    private Student _student = new();

    public override string Title => _student.Id == 0 ? "Nouvel élève" : "Modifier l'élève";
    public override double Width => 640;
    public int? SavedId { get; private set; }

    public IReadOnlyList<Option<Gender>> Genders => Options.Genders;

    [ObservableProperty] private string _matricule = "";
    [ObservableProperty] private string _firstName = "";
    [ObservableProperty] private string _lastName = "";
    [ObservableProperty] private DateTime? _birthDate;
    [ObservableProperty] private Option<Gender>? _gender;
    [ObservableProperty] private IReadOnlyList<string> _levels = Options.DefaultLevels;
    [ObservableProperty] private string _level = "";
    [ObservableProperty] private string? _school;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private DateTime? _enrolledOn = DateTime.Today;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string? _photoFile;
    [ObservableProperty] private string? _photoPath;

    [ObservableProperty] private IReadOnlyList<Option<int?>> _parents = [];
    [ObservableProperty] private Option<int?>? _selectedParent;
    [ObservableProperty] private bool _createParent;
    [ObservableProperty] private string? _newParentName;
    [ObservableProperty] private string? _newParentPhone;
    [ObservableProperty] private string? _newParentRelation = "Père";

    [ObservableProperty] private IReadOnlyList<Option<int?>> _discountOptions = [];
    [ObservableProperty] private Option<int?>? _selectedDiscount;

    public async Task InitializeAsync(int? id)
    {
        _student = id is null ? new Student { EnrolledOn = clock.GetLocalNow().Date } : await students.GetAsync(id.Value) ?? throw new BusinessException("Élève introuvable.");
        OnPropertyChanged(nameof(Title));
        var s = _student;
        Matricule = s.Id == 0 ? await students.NextMatriculeAsync() : s.Matricule;
        FirstName = s.FirstName;
        LastName = s.LastName;
        BirthDate = s.BirthDate;
        Gender = Genders.First(g => g.Value == s.Gender);
        var levels = await students.LevelsAsync();
        Levels = Options.DefaultLevels.Union(levels).OrderBy(Levels_Order).ToList();
        Level = s.Level;
        School = s.School;
        Phone = s.Phone;
        Address = s.Address;
        EnrolledOn = s.EnrolledOn;
        IsActive = s.IsActive;
        Notes = s.Notes;
        PhotoFile = s.PhotoFile;
        PhotoPath = s.PhotoFile is null ? null : storage.GetPath(s.PhotoFile, StorageAreas.Images);

        Parents = [new Option<int?>(null, "Aucun"), .. (await parents.ListAsync()).Select(p => new Option<int?>(p.Id, string.IsNullOrWhiteSpace(p.Phone) ? p.FullName : $"{p.FullName} · {p.Phone}"))];
        SelectedParent = Parents.FirstOrDefault(p => p.Value == s.ParentId) ?? Parents[0];
        DiscountOptions = [new Option<int?>(null, "Aucune"), .. (await discounts.ListAsync()).Where(d => d.IsActive || d.Id == s.DiscountId).Select(d => new Option<int?>(d.Id, d.ToString()))];
        SelectedDiscount = DiscountOptions.FirstOrDefault(d => d.Value == s.DiscountId) ?? DiscountOptions[0];
    }

    private static int Levels_Order(string l) => Domain.Calculations.Levels.Order(l);

    [RelayCommand]
    private void PickPhoto()
    {
        var path = files.OpenFile("Choisir une photo", "Images|*.jpg;*.jpeg;*.png;*.bmp");
        if (path is null) return;
        PhotoFile = storage.Import(path, StorageAreas.Images);
        PhotoPath = storage.GetPath(PhotoFile, StorageAreas.Images);
    }

    [RelayCommand]
    private void RemovePhoto()
    {
        PhotoFile = null;
        PhotoPath = null;
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        int? parentId = SelectedParent?.Value;
        if (CreateParent)
        {
            if (string.IsNullOrWhiteSpace(NewParentName)) throw new BusinessException("Saisissez le nom du nouveau parent.");
            var p = await parents.SaveAsync(new Parent { FullName = NewParentName.Trim(), Phone = NewParentPhone, Relation = NewParentRelation, Address = Address });
            parentId = p.Id;
        }
        var s = _student;
        s.Matricule = Matricule.Trim();
        s.FirstName = FirstName;
        s.LastName = LastName;
        s.BirthDate = BirthDate;
        s.Gender = Gender?.Value ?? Domain.Enums.Gender.Unspecified;
        s.Level = (Level ?? "").Trim().ToUpperInvariant();
        s.School = School;
        s.Phone = Phone;
        s.Address = Address;
        s.EnrolledOn = (EnrolledOn ?? throw new BusinessException("Saisissez la date d'inscription.")).Date;
        s.IsActive = IsActive;
        s.Notes = Notes;
        s.PhotoFile = PhotoFile;
        s.ParentId = parentId;
        s.DiscountId = SelectedDiscount?.Value;
        var saved = await students.SaveAsync(s);
        SavedId = saved.Id;
        return true;
    }
}

/// <summary>Enroll a student in a group, or move them from one group to another.</summary>
public sealed partial class EnrollDialogViewModel(IStudentService students, IGroupService groups, TimeProvider clock) : DialogViewModel
{
    private int _studentId;
    private int? _fromGroupId;

    public override string Title => _fromGroupId is null ? "Inscrire à un cours" : "Changer de groupe";
    public override string ConfirmText => _fromGroupId is null ? "Inscrire" : "Changer de groupe";

    [ObservableProperty] private string _studentName = "";
    [ObservableProperty] private string? _fromGroup;
    [ObservableProperty] private bool _showAllLevels;
    [ObservableProperty] private IReadOnlyList<Option<int>> _groupOptions = [];
    [ObservableProperty] private Option<int>? _selectedGroup;
    private List<Group> _groups = [];
    private string _level = "";

    public async Task InitializeAsync(int studentId, int? fromGroupId)
    {
        _studentId = studentId;
        _fromGroupId = fromGroupId;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ConfirmText));
        var s = await students.GetAsync(studentId) ?? throw new BusinessException("Élève introuvable.");
        StudentName = s.FullName;
        _level = s.Level;
        _groups = (await groups.ListAsync()).Where(g => g.IsActive && g.Course!.IsActive).ToList();
        var from = _groups.FirstOrDefault(g => g.Id == fromGroupId);
        FromGroup = from?.FullName;
        if (from is not null) _level = from.Course!.Level;
        Refresh();
    }

    partial void OnShowAllLevelsChanged(bool value) => Refresh();

    private void Refresh()
    {
        var today = clock.GetLocalNow().Date;
        GroupOptions = _groups
            .Where(g => g.Id != _fromGroupId && (ShowAllLevels || g.Course!.Level == _level))
            .Where(g => !g.Enrollments.Any(e => e.StudentId == _studentId && e.IsActiveOn(today)))
            .Select(g =>
            {
                var n = g.Enrollments.Count(e => e.IsActiveOn(today));
                var full = n >= g.Capacity ? " · complet" : "";
                return new Option<int>(g.Id, $"{g.FullName} · {g.Teacher?.FullName ?? "sans enseignant"} · {n}/{g.Capacity}{full} · {Money.Format(g.Course!.MonthlyPrice)}");
            }).ToList();
        SelectedGroup = GroupOptions.FirstOrDefault();
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (SelectedGroup is null) throw new BusinessException("Choisissez un groupe.");
        if (_fromGroupId is { } from) await students.ChangeGroupAsync(_studentId, from, SelectedGroup.Value);
        else await students.EnrollAsync(_studentId, SelectedGroup.Value);
        return true;
    }
}

public sealed partial class DiscountPickerDialogViewModel(IStudentService students, ICrudService<Discount> discounts) : DialogViewModel
{
    private int _studentId;

    public override string Title => "Appliquer une remise";
    public override string ConfirmText => "Appliquer";

    [ObservableProperty] private IReadOnlyList<Option<int?>> _discountOptions = [];
    [ObservableProperty] private Option<int?>? _selectedDiscount;

    public async Task InitializeAsync(int studentId, int? currentDiscountId)
    {
        _studentId = studentId;
        DiscountOptions = [new Option<int?>(null, "Aucune remise"), .. (await discounts.ListAsync()).Where(d => d.IsActive).Select(d => new Option<int?>(d.Id, d.ToString()))];
        SelectedDiscount = DiscountOptions.FirstOrDefault(d => d.Value == currentDiscountId) ?? DiscountOptions[0];
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        await students.SetDiscountAsync(_studentId, SelectedDiscount?.Value);
        return true;
    }
}

/// <summary>Attach a file to a student, teacher, parent or the center. The file is picked before the dialog opens.</summary>
public sealed partial class AddDocumentDialogViewModel(IDocumentService documents, IFilePicker files) : DialogViewModel
{
    private DocumentOwnerType _ownerType;
    private int? _ownerId;
    private string? _path;

    public override string Title => "Ajouter un document";
    public override string ConfirmText => "Ajouter";
    public bool HasFile => _path is not null;
    public IReadOnlyList<string> Categories => Options.DocumentCategories;

    [ObservableProperty] private string _ownerLabel = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _documentTitle = "";
    [ObservableProperty] private string? _category = "Autre";

    public void Initialize(DocumentOwnerType ownerType, int? ownerId, string ownerName)
    {
        _ownerType = ownerType;
        _ownerId = ownerId;
        OwnerLabel = ownerType == DocumentOwnerType.Center ? "Centre" : ownerName;
        _path = files.OpenFile("Choisir un document", "Documents|*.pdf;*.jpg;*.jpeg;*.png;*.doc;*.docx;*.xls;*.xlsx;*.txt|Tous les fichiers|*.*");
        if (_path is null) return;
        FileName = Path.GetFileName(_path);
        DocumentTitle = Path.GetFileNameWithoutExtension(_path);
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        await documents.AddAsync(_path!, DocumentTitle, Category, _ownerType, _ownerId);
        return true;
    }
}
