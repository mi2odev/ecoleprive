using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.Core;

/// <summary>Base class of modal dialogs rendered in the window overlay.</summary>
public abstract partial class DialogViewModel : ViewModelBase
{
    private TaskCompletionSource<bool>? _tcs;

    public abstract string Title { get; }
    public virtual string ConfirmText => "Enregistrer";
    public virtual string CancelText => "Annuler";
    public virtual bool IsDanger => false;
    /// <summary>Dialog width in pixels.</summary>
    public virtual double Width => 480;

    internal Task<bool> Open()
    {
        _tcs = new TaskCompletionSource<bool>();
        return _tcs.Task;
    }

    /// <summary>Validates and performs the dialog's action. Return false to keep the dialog open.</summary>
    protected virtual Task<bool> OnConfirmAsync() => Task.FromResult(true);

    [RelayCommand]
    private async Task Confirm()
    {
        if (IsBusy) return;
        var ok = false;
        await RunAsync(async () => ok = await OnConfirmAsync());
        if (ok) Close(true);
    }

    [RelayCommand]
    public void Cancel() => Close(false);

    public event EventHandler? Closed;

    private void Close(bool result)
    {
        Closed?.Invoke(this, EventArgs.Empty);
        _tcs?.TrySetResult(result);
    }
}

public sealed class ConfirmDialogViewModel(string title, string message, string confirmText, bool danger) : DialogViewModel
{
    public override string Title => title;
    public string Message => message;
    public override string ConfirmText => confirmText;
    public override bool IsDanger => danger;
    public override double Width => 440;
}

/// <summary>Hosts the dialog currently shown on top of the window (one at a time, stacked if nested).</summary>
public sealed partial class DialogHost : ObservableObject
{
    private readonly Stack<DialogViewModel> _stack = new();

    [ObservableProperty]
    private DialogViewModel? _current;

    public async Task<bool> ShowAsync(DialogViewModel dialog)
    {
        if (Current is not null) _stack.Push(Current);
        Current = dialog;
        var task = dialog.Open();
        dialog.Closed += OnClosed;
        return await task;

        void OnClosed(object? s, EventArgs e)
        {
            dialog.Closed -= OnClosed;
            Current = _stack.Count > 0 ? _stack.Pop() : null;
        }
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Supprimer", bool danger = true) =>
        ShowAsync(new ConfirmDialogViewModel(title, message, confirmText, danger));

    public void CloseAll()
    {
        while (Current is not null) Current.Cancel();
    }
}
