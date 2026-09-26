using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Dialogs;

/// <summary>One absent student of the "Prévenir les parents" dialog.</summary>
public sealed record AbsenceNoticeLine(int StudentId, string Student, string Parent, string Phone, string Message, bool CanWhatsApp,
    IRelayCommand WhatsApp, IRelayCommand Copy);

/// <summary>
/// Absence notices of a session: the filled message for each absent student's parent, ready to send through
/// WhatsApp (wa.me link) or to paste in an SMS. Nothing is sent by the application itself.
/// </summary>
public sealed partial class AbsenceNoticeDialogViewModel(
    IStudentService students, ISettingsService settings, IClipboard clipboard, ILauncher launcher, INotifier notifier) : DialogViewModel
{
    public override string Title => "Prévenir les parents";
    public override string ConfirmText => "Copier tous les messages";
    public override string CancelText => "Fermer";
    public override double Width => 760;

    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private IReadOnlyList<AbsenceNoticeLine> _lines = [];

    /// <param name="course">Course name for {cours}, e.g. "Mathématiques".</param>
    /// <param name="sessionLabel">Session shown in the dialog header, e.g. "Mathématiques · 3AS A".</param>
    public async Task InitializeAsync(string course, string sessionLabel, DateTime date, IReadOnlyCollection<int> absentStudentIds)
    {
        var cfg = await settings.GetAsync();
        var all = await students.ListAsync(date);
        var absents = all.Where(s => absentStudentIds.Contains(s.Id)).ToList();
        Subtitle = $"{sessionLabel} · {Labels.LongDate(date)} · {absents.Count} absent{(absents.Count > 1 ? "s" : "")}";
        Lines = absents.Select(s =>
        {
            var message = MessageTemplates.AbsenceNotice(cfg, s.ParentName, s.FullName, course, date);
            var url = MessageTemplates.WhatsAppUrl(s.ParentPhone, cfg.PhoneCountryCode, message);
            return new AbsenceNoticeLine(s.Id, s.FullName, s.ParentName ?? "Aucun parent", s.ParentPhone ?? "Pas de téléphone", message, url is not null,
                new RelayCommand(() => Open(url)),
                new RelayCommand(() => Copy(message, "Message copié · " + s.FullName)));
        }).ToList();
    }

    private void Open(string? url)
    {
        if (url is null)
        {
            notifier.Error("Numéro de téléphone du parent manquant ou invalide");
            return;
        }
        Try(() => launcher.OpenUrl(url));
    }

    private void Copy(string text, string done)
    {
        if (Try(() => clipboard.SetText(text))) notifier.Info(done);
    }

    private bool Try(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (BusinessException ex)
        {
            notifier.Error(ex.Message);
            return false;
        }
    }

    /// <summary>Text of every notice, each preceded by the student, parent and phone.</summary>
    public string AllMessages => string.Join("\n\n", Lines.Select(l => $"— {l.Student} · {l.Parent} · {l.Phone}\n{l.Message}"));

    protected override Task<bool> OnConfirmAsync()
    {
        if (Lines.Count == 0) return Task.FromResult(true);
        clipboard.SetText(AllMessages);
        notifier.Info($"{Lines.Count} message{(Lines.Count > 1 ? "s" : "")} copié{(Lines.Count > 1 ? "s" : "")}");
        return Task.FromResult(true);
    }
}
