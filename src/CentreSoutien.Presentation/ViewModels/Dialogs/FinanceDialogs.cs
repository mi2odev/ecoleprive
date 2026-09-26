using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Dialogs;

/// <summary>Create or edit a discount (percentage or fixed amount off the monthly fee).</summary>
public sealed partial class DiscountEditorDialogViewModel(ICrudService<Discount> discounts) : DialogViewModel
{
    private Discount _discount = new();

    public override string Title => _discount.Id == 0 ? "Nouvelle remise" : "Modifier la remise";
    public IReadOnlyList<Option<DiscountType>> Types => Options.DiscountTypes;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private Option<DiscountType>? _selectedType;
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string _valueHint = "";

    public int? SavedId { get; private set; }

    public void Initialize(Discount? discount)
    {
        _discount = discount ?? new Discount();
        OnPropertyChanged(nameof(Title));
        Name = _discount.Name;
        SelectedType = Types.First(t => t.Value == _discount.Type);
        Value = _discount.Id == 0 ? "" : _discount.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        IsActive = _discount.IsActive;
        Notes = _discount.Notes;
    }

    partial void OnSelectedTypeChanged(Option<DiscountType>? value) =>
        ValueHint = value?.Value == DiscountType.FixedAmount ? $"Montant retiré de la mensualité ({Money.Currency})" : "Pourcentage retiré de la mensualité (0 à 100)";

    protected override async Task<bool> OnConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new BusinessException("Le nom de la remise est obligatoire.");
        var value = Parse.Amount(Value) ?? throw new BusinessException("Saisissez la valeur de la remise.");
        var d = _discount;
        d.Name = Name.Trim();
        d.Type = SelectedType?.Value ?? DiscountType.Percent;
        d.Value = value;
        d.IsActive = IsActive;
        d.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        var saved = await discounts.SaveAsync(d);
        SavedId = saved.Id;
        return true;
    }
}

/// <summary>Create or edit an expense of the center.</summary>
public sealed partial class ExpenseEditorDialogViewModel(ICrudService<Expense> expenses) : DialogViewModel
{
    private Expense _expense = new();

    public override string Title => _expense.Id == 0 ? "Ajouter une dépense" : "Modifier la dépense";
    public IReadOnlyList<string> Categories => Options.ExpenseCategories;
    public IReadOnlyList<Option<PaymentMethod>> Methods => Options.PaymentMethods;

    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string _category = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string? _supplier;
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private Option<PaymentMethod>? _selectedMethod;

    public void Initialize(Expense? expense, DateTime defaultDate)
    {
        _expense = expense ?? new Expense { Date = defaultDate.Date, Category = Options.ExpenseCategories[0] };
        OnPropertyChanged(nameof(Title));
        Date = _expense.Date;
        Category = _expense.Category;
        Description = _expense.Description;
        Supplier = _expense.Supplier;
        Amount = _expense.Id == 0 ? "" : Money.Number(_expense.Amount);
        SelectedMethod = Methods.First(m => m.Value == _expense.Method);
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(Category)) throw new BusinessException("Choisissez une catégorie.");
        var amount = Parse.Amount(Amount) ?? 0;
        if (amount <= 0) throw new BusinessException("Saisissez un montant.");
        var e = _expense;
        e.Date = Date.Date;
        e.Category = Category.Trim();
        e.Description = (Description ?? "").Trim();
        e.Supplier = string.IsNullOrWhiteSpace(Supplier) ? null : Supplier.Trim();
        e.Amount = amount;
        e.Method = SelectedMethod?.Value ?? PaymentMethod.Cash;
        await expenses.SaveAsync(e);
        return true;
    }
}

/// <summary>Restores an encrypted backup file. The current data is replaced; the owner signs in again afterwards.</summary>
public sealed partial class RestoreBackupDialogViewModel(IBackupService backup, IFilePicker files) : DialogViewModel
{
    public override string Title => "Restaurer une sauvegarde";
    public override string ConfirmText => "Restaurer";
    public override bool IsDanger => true;
    public override double Width => 520;

    public string Warning =>
        "Toutes les données actuelles (élèves, paiements, documents…) seront remplacées par celles de la sauvegarde. " +
        "Une copie de la base actuelle est conservée dans le dossier des sauvegardes. Vous devrez ensuite vous reconnecter.";

    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private string _password = "";

    public bool HasFile => !string.IsNullOrEmpty(FilePath);
    partial void OnFilePathChanged(string? value) => OnPropertyChanged(nameof(HasFile));

    [RelayCommand]
    public void PickFile()
    {
        var path = files.OpenFile("Choisir une sauvegarde", "Sauvegarde du centre|*.csbak|Tous les fichiers|*.*");
        if (path is not null) FilePath = path;
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(FilePath)) throw new BusinessException("Choisissez un fichier de sauvegarde.");
        if (string.IsNullOrEmpty(Password)) throw new BusinessException("Saisissez le mot de passe en vigueur au moment de la sauvegarde.");
        await backup.RestoreAsync(FilePath, Password);
        Password = "";
        return true;
    }
}
