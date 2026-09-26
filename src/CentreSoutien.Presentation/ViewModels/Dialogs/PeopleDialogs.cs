using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Dialogs;

/// <summary>Create or edit a parent / guardian.</summary>
public sealed partial class ParentEditorDialogViewModel(IParentService parents) : DialogViewModel
{
    private Parent _parent = new();

    public override string Title => _parent.Id == 0 ? "Nouveau parent" : "Modifier le parent";
    public override double Width => 560;
    public int? SavedId { get; private set; }

    public IReadOnlyList<string> Relations { get; } = ["Père", "Mère", "Tuteur"];

    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string? _relation = "Père";
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _phone2;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _profession;
    [ObservableProperty] private string? _notes;

    public async Task InitializeAsync(int? id)
    {
        _parent = id is null ? new Parent() : await parents.GetAsync(id.Value) ?? throw new BusinessException("Parent introuvable.");
        OnPropertyChanged(nameof(Title));
        var p = _parent;
        FullName = p.FullName;
        Relation = p.Relation;
        Phone = p.Phone;
        Phone2 = p.Phone2;
        Email = p.Email;
        Address = p.Address;
        Profession = p.Profession;
        Notes = p.Notes;
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(FullName)) throw new BusinessException("Le nom du parent est obligatoire.");
        var p = _parent;
        p.FullName = FullName.Trim();
        p.Relation = Clean(Relation);
        p.Phone = Clean(Phone);
        p.Phone2 = Clean(Phone2);
        p.Email = Clean(Email);
        p.Address = Clean(Address);
        p.Profession = Clean(Profession);
        p.Notes = Clean(Notes);
        var saved = await parents.SaveAsync(p);
        SavedId = saved.Id;
        return true;
    }

    internal static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Create or edit a teacher (identity, subject, compensation rule, photo).</summary>
public sealed partial class TeacherEditorDialogViewModel(
    ITeacherService teachers, ICrudService<Subject> subjects, ISettingsService settings, IFileStorage storage, IFilePicker files, TimeProvider clock) : DialogViewModel
{
    private Teacher _teacher = new();
    private CenterSettings _settings = new();
    private bool _loading;

    public override string Title => _teacher.Id == 0 ? "Nouvel enseignant" : "Modifier l'enseignant";
    public override double Width => 640;
    public int? SavedId { get; private set; }

    public IReadOnlyList<Option<CompensationType>> CompensationTypes => Options.CompensationTypes;

    [ObservableProperty] private string _firstName = "";
    [ObservableProperty] private string _lastName = "";
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private IReadOnlyList<Option<int?>> _subjectOptions = [];
    [ObservableProperty] private Option<int?>? _selectedSubject;
    [ObservableProperty] private string _startYear = "";
    [ObservableProperty] private Option<CompensationType>? _selectedCompensation;
    [ObservableProperty] private string _compensationValue = "";
    [ObservableProperty] private string _valueLabel = "";
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string? _photoFile;
    [ObservableProperty] private string? _photoPath;

    public async Task InitializeAsync(int? id)
    {
        _loading = true;
        try
        {
            _settings = await settings.GetAsync();
            _teacher = id is null
                ? new Teacher
                {
                    StartYear = clock.GetLocalNow().Year,
                    CompensationType = _settings.DefaultCompensationType,
                    CompensationValue = DefaultValue(_settings.DefaultCompensationType),
                }
                : await teachers.GetAsync(id.Value) ?? throw new BusinessException("Enseignant introuvable.");
            OnPropertyChanged(nameof(Title));
            var t = _teacher;
            FirstName = t.FirstName;
            LastName = t.LastName;
            Phone = t.Phone;
            Email = t.Email;
            Address = t.Address;
            SubjectOptions = [new Option<int?>(null, "Aucune"), .. (await subjects.ListAsync()).OrderBy(s => s.Name).Select(s => new Option<int?>(s.Id, s.Name))];
            SelectedSubject = SubjectOptions.FirstOrDefault(s => s.Value == t.SubjectId) ?? SubjectOptions[0];
            StartYear = t.StartYear.ToString();
            SelectedCompensation = CompensationTypes.First(c => c.Value == t.CompensationType);
            CompensationValue = Domain.Calculations.Money.Number(t.CompensationValue);
            IsActive = t.IsActive;
            Notes = t.Notes;
            PhotoFile = t.PhotoFile;
            PhotoPath = t.PhotoFile is null ? null : storage.GetPath(t.PhotoFile, StorageAreas.Images);
            UpdateValueLabel();
        }
        finally
        {
            _loading = false;
        }
    }

    private decimal DefaultValue(CompensationType type) => type switch
    {
        CompensationType.Percentage => _settings.DefaultCompensationPercent,
        CompensationType.PerSession => _settings.DefaultSessionRate,
        _ => _settings.DefaultMonthlySalary,
    };

    partial void OnSelectedCompensationChanged(Option<CompensationType>? value)
    {
        UpdateValueLabel();
        if (_loading || value is null) return;
        CompensationValue = Domain.Calculations.Money.Number(DefaultValue(value.Value));
    }

    private void UpdateValueLabel() => ValueLabel = SelectedCompensation?.Value switch
    {
        CompensationType.Percentage => "Pourcentage (%)",
        CompensationType.PerSession => "Montant par séance (DZD)",
        _ => "Montant mensuel (DZD)",
    };

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
        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
            throw new BusinessException("Le nom et le prénom de l'enseignant sont obligatoires.");
        if (!int.TryParse(StartYear?.Trim(), out var year) || year < 1950 || year > clock.GetLocalNow().Year + 1)
            throw new BusinessException("Année d'arrivée invalide.");
        var value = Parse.Amount(CompensationValue) ?? throw new BusinessException("Saisissez la valeur de la rémunération.");
        var t = _teacher;
        t.FirstName = FirstName.Trim();
        t.LastName = LastName.Trim();
        t.Phone = ParentEditorDialogViewModel.Clean(Phone);
        t.Email = ParentEditorDialogViewModel.Clean(Email);
        t.Address = ParentEditorDialogViewModel.Clean(Address);
        t.SubjectId = SelectedSubject?.Value;
        t.StartYear = year;
        t.CompensationType = SelectedCompensation?.Value ?? CompensationType.Percentage;
        t.CompensationValue = value;
        t.IsActive = IsActive;
        t.Notes = ParentEditorDialogViewModel.Clean(Notes);
        t.PhotoFile = PhotoFile;
        var saved = await teachers.SaveAsync(t);
        SavedId = saved.Id;
        return true;
    }
}
