using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CentreSoutien.Presentation.ViewModels.Pages;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Tests.Presentation;

public class MessageTemplatesTests
{
    [Theory]
    [InlineData("0661 24 18 90", "213661241890")]
    [InlineData("0661-24-18-90", "213661241890")]
    [InlineData("+213 661 24 18 90", "213661241890")]
    [InlineData("00213661241890", "213661241890")]
    [InlineData("213661241890", "213661241890")]
    [InlineData("661241890", "213661241890")]
    [InlineData("+213 (0)661 24 18 90", "213661241890")]
    [InlineData("021 23 45 67", "21321234567")]
    [InlineData("0661 24 18 90 / 0555 11 22 33", "213661241890")]
    [InlineData("+33 6 12 34 56 78", "33612345678")]
    public void Phone_numbers_are_normalised_to_international_format(string input, string expected) =>
        Assert.Equal(expected, MessageTemplates.NormalizePhone(input, "213"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pas de téléphone")]
    [InlineData("0661")]
    [InlineData("12")]
    [InlineData("+1234567890123456789")]
    public void Empty_or_invalid_numbers_are_ignored(string? input) => Assert.Null(MessageTemplates.NormalizePhone(input, "213"));

    [Fact]
    public void Country_code_comes_from_settings()
    {
        Assert.Equal("33612345678", MessageTemplates.NormalizePhone("06 12 34 56 78", "33"));
        Assert.Equal("33612345678", MessageTemplates.NormalizePhone("06 12 34 56 78", "+33"));
        Assert.Equal("213661241890", MessageTemplates.NormalizePhone("0661241890", "")); // falls back to Algeria
    }

    [Fact]
    public void Placeholders_are_filled_case_and_accent_insensitive()
    {
        var text = MessageTemplates.Fill("{Parent} / {élève} / {ELEVE} / {mois} / {reste} / {mensualite} / {mensualité} / {cours} / {date} / {centre} / {telephone} / {inconnu}",
            new MessageValues
            {
                Parent = "M. Benali", Student = "Yacine", Month = "septembre 2026", Balance = "2 000 DZD", MonthlyFee = "4 500 DZD",
                Course = "Maths", Date = "samedi 26 septembre 2026", Center = "FLA", Phone = "021 00 00 00",
            });
        Assert.Equal("M. Benali / Yacine / Yacine / septembre 2026 / 2 000 DZD / 4 500 DZD / 4 500 DZD / Maths / samedi 26 septembre 2026 / FLA / 021 00 00 00 / {inconnu}", text);
    }

    [Fact]
    public void Missing_values_leave_no_dangling_separator()
    {
        var text = MessageTemplates.Fill("Bonjour {parent}, {eleve}  absent. {centre} – {telephone}", new MessageValues { Student = "Yacine", Center = "FLA" });
        Assert.Equal("Bonjour Madame, Monsieur, Yacine absent. FLA", text);
        Assert.Equal("", MessageTemplates.Fill("  ", new MessageValues()));
    }

    [Fact]
    public void Default_templates_produce_the_expected_messages()
    {
        var s = new CenterSettings { CenterName = "Future Leaders Academy", Phone = "021 45 67 89" };
        var reminder = MessageTemplates.PaymentReminder(s, "Karim Benali", "Yacine Benali", new DateTime(2026, 9, 1), 4500, 2000);
        Assert.Equal("Bonjour Karim Benali, sauf erreur de notre part, la mensualité de septembre 2026 pour Yacine Benali n'est pas encore réglée. " +
                     "Reste à payer : 2 000 DZD. Merci de passer au centre. Future Leaders Academy – 021 45 67 89", reminder);

        var absence = MessageTemplates.AbsenceNotice(s, null, "Yacine Benali", "Mathématiques", new DateTime(2026, 9, 26));
        Assert.Equal("Bonjour Madame, Monsieur, nous vous informons que Yacine Benali était absent(e) au cours de Mathématiques le samedi 26 septembre 2026. Future Leaders Academy", absence);

        // An emptied template falls back to the default text.
        s.AbsenceMessageTemplate = "";
        Assert.Equal(absence, MessageTemplates.AbsenceNotice(s, null, "Yacine Benali", "Mathématiques", new DateTime(2026, 9, 26)));
    }

    [Fact]
    public void WhatsApp_link_is_url_encoded()
    {
        Assert.Equal("https://wa.me/213661241890?text=Bonjour%20M.%20%26%20Mme%2C%20r%C3%A9gl%C3%A9e",
            MessageTemplates.WhatsAppUrl("0661 24 18 90", "213", "Bonjour M. & Mme, réglée"));
        Assert.Null(MessageTemplates.WhatsAppUrl("", "213", "x"));
    }
}

public class RemindersTests
{
    private static async Task<PaymentsViewModel> OpenRemindersAsync(TestHost host)
    {
        await host.Get<Navigator>().NavigateAsync<PaymentsViewModel>();
        var page = host.Page<PaymentsViewModel>();
        Assert.False(page.HasError, page.Error);
        page.ShowTabCommand.Execute("reminders");
        Assert.True(page.IsReminders);
        return page;
    }

    [Fact]
    public async Task Migration_adds_the_message_settings_with_their_defaults()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var s = await host.Get<ISettingsService>().GetAsync();
        Assert.Equal(CenterSettings.DefaultPaymentReminderTemplate, s.PaymentReminderTemplate);
        Assert.Equal(CenterSettings.DefaultAbsenceMessageTemplate, s.AbsenceMessageTemplate);
        Assert.Equal("213", s.PhoneCountryCode);
    }

    [Fact]
    public async Task Reminders_list_students_with_a_balance_and_their_parent()
    {
        await using var host = await UiHost.CreateAsync();
        var page = await OpenRemindersAsync(host);

        var unpaid = page.Rows.Where(r => r.BalanceValue > 0).Select(r => r.StudentId).Order().ToList();
        Assert.NotEmpty(unpaid);
        Assert.Equal(unpaid, page.Reminders.Select(r => r.StudentId).Order());
        Assert.All(page.Reminders, r => Assert.True(r.IsSelected));
        Assert.Equal($"{page.Reminders.Count} sélectionnés", page.SelectionLabel);
        Assert.Equal("Échéance le 05/09/2026", page.DueDateLabel);

        // 26/09 vs due day 5 → 21 days late, beyond the 10-day reminder threshold.
        var line = page.Reminders.First(r => r.ParentName is not null && r.CanWhatsApp);
        Assert.Equal(21, line.DaysLate);
        Assert.Equal("21 j de retard", line.DelayLabel);
        Assert.Equal(BadgeKind.Bad, line.Delay.Kind);
        Assert.StartsWith($"Bonjour {line.ParentName}, sauf erreur de notre part, la mensualité de septembre 2026 pour {line.Name}", line.Message);
        Assert.Contains($"Reste à payer : {line.Rest}.", line.Message);
        Assert.StartsWith("https://wa.me/213", line.WhatsAppUrl);
    }

    [Fact]
    public async Task Row_actions_copy_the_message_open_whatsapp_and_copy_the_sms_text()
    {
        await using var host = await UiHost.CreateAsync();
        var fake = host.Get<FakePlatform>();
        var page = await OpenRemindersAsync(host);
        var line = page.Reminders.First(r => r.CanWhatsApp);

        line.Copy.Execute(null);
        Assert.Equal(line.Message, fake.Clipboard);

        line.WhatsApp.Execute(null);
        var url = Assert.Single(fake.OpenedUrls);
        Assert.Equal(line.WhatsAppUrl, url);
        Assert.Contains("?text=Bonjour%20", url);

        line.Sms.Execute(null);
        Assert.Equal(line.ShortMessage, fake.Clipboard);
        Assert.Contains(line.Rest, line.ShortMessage);
        Assert.True(line.ShortMessage.Length < line.Message.Length);
    }

    [Fact]
    public async Task Bulk_actions_follow_the_selection()
    {
        await using var host = await UiHost.CreateAsync();
        var fake = host.Get<FakePlatform>();
        var page = await OpenRemindersAsync(host);
        Assert.True(page.Reminders.Count >= 3);

        page.AllRemindersSelected = false;
        Assert.All(page.Reminders, r => Assert.False(r.IsSelected));
        Assert.Equal("Aucun élève sélectionné", page.SelectionLabel);
        await page.PrintReminderLettersCommand.ExecuteAsync(null);
        Assert.Empty(fake.PrintedPages);

        page.Reminders[0].IsSelected = true;
        page.Reminders[2].IsSelected = true;
        Assert.Null(page.AllRemindersSelected);
        Assert.Equal("2 sélectionnés", page.SelectionLabel);

        await page.PrintReminderLettersCommand.ExecuteAsync(null);
        Assert.False(page.HasError, page.Error);
        var (job, pages) = Assert.Single(fake.PrintedPages);
        Assert.Equal("Relances 2026-09", job);
        Assert.Equal(2, pages.Count);
        Assert.All(pages, p => Assert.Equal("Rappel de paiement", p.Title));
        Assert.Contains(pages[0].Blocks, b => b is PrintParagraph p && p.Text == page.Reminders[0].Message);
        Assert.Contains(pages[1].Blocks, b => b is PrintFields f && f.Rows.Contains(("Reste à payer", page.Reminders[2].Rest)));
        Assert.Contains(pages[0].Blocks, b => b is PrintSignature);

        page.CopyAllRemindersCommand.Execute(null);
        Assert.Contains(page.Reminders[0].Message, fake.Clipboard);
        Assert.Contains(page.Reminders[2].Message, fake.Clipboard);
        Assert.DoesNotContain(page.Reminders[1].Message, fake.Clipboard);

        var path = Path.Combine(host.Folder, "relances.xlsx");
        fake.NextSaveFile = path;
        await page.ExportRemindersCommand.ExecuteAsync(null);
        Assert.False(page.HasError, page.Error);
        Assert.True(File.Exists(path));

        page.AllRemindersSelected = true;
        Assert.All(page.Reminders, r => Assert.True(r.IsSelected));
    }

    [Fact]
    public async Task Reminders_use_the_templates_edited_in_settings()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<SettingsViewModel>();
        var settings = host.Page<SettingsViewModel>();
        settings.SelectedSection = settings.Sections.Single(s => s.Value == "messages");
        Assert.True(settings.IsMessages);
        Assert.Equal(CenterSettings.DefaultPaymentReminderTemplate, settings.PaymentTemplate);
        Assert.Contains("{parent}", settings.PlaceholderHelp);
        Assert.StartsWith("Bonjour M. Benali, sauf erreur", settings.PaymentPreview);

        settings.PaymentTemplate = "Rappel {eleve} : {reste} ({mois})";
        Assert.StartsWith("Rappel Yacine Benali : 2 000 DZD", settings.PaymentPreview);
        settings.Form.PhoneCountryCode = "+33";
        await settings.SaveCommand.ExecuteAsync(null);
        Assert.False(settings.HasError, settings.Error);
        var saved = await host.Get<ISettingsService>().GetAsync();
        Assert.Equal("Rappel {eleve} : {reste} ({mois})", saved.PaymentReminderTemplate);
        Assert.Equal("33", saved.PhoneCountryCode);

        var page = await OpenRemindersAsync(host);
        var line = page.Reminders.First(r => r.CanWhatsApp);
        Assert.Equal($"Rappel {line.Name} : {line.Rest} (septembre 2026)", line.Message);
        Assert.StartsWith("https://wa.me/33", line.WhatsAppUrl);

        // Reset to the default text and reject an invalid country code.
        settings.ResetPaymentTemplateCommand.Execute(null);
        Assert.Equal(CenterSettings.DefaultPaymentReminderTemplate, settings.Form.PaymentReminderTemplate);
        settings.Form.PhoneCountryCode = "abc";
        await settings.SaveCommand.ExecuteAsync(null);
        Assert.True(settings.HasError);
    }

    [Fact]
    public async Task Attendance_offers_absence_notices_after_saving()
    {
        await using var host = await UiHost.CreateAsync();
        var fake = host.Get<FakePlatform>();
        await host.Get<Navigator>().NavigateAsync<AttendanceViewModel>();
        var page = host.Page<AttendanceViewModel>();
        var todo = page.SessionItems.First(i => i.Progress == "À faire");
        await ((IAsyncRelayCommand)todo.Select).ExecuteAsync(null);
        Assert.False(page.CanNotifyParents);

        page.AllPresentCommand.Execute(null);
        page.Rows[0].Status = AttendanceStatus.Absent;
        page.Rows[1].Status = AttendanceStatus.Absent;
        Assert.False(page.CanNotifyParents); // only saved absences count
        await page.SaveCommand.ExecuteAsync(null);
        Assert.Null(host.Get<DialogHost>().Current); // NotifyParentOnAbsence is off by default
        Assert.True(page.CanNotifyParents);
        Assert.Equal("Prévenir les parents (2 absents)", page.NotifyParentsLabel);

        var running = page.NotifyParentsCommand.ExecuteAsync(null);
        var dialog = await WaitForDialogAsync(host, running);
        Assert.Equal(2, dialog.Lines.Count);
        Assert.Equal(new[] { page.Rows[0].Name, page.Rows[1].Name }.Order(), dialog.Lines.Select(l => l.Student).Order());
        Assert.Contains("2 absents", dialog.Subtitle);
        var line = dialog.Lines.First(l => l.CanWhatsApp);
        Assert.Contains($"nous vous informons que {line.Student} était absent(e) au cours de ", line.Message);
        Assert.Contains(" le samedi 26 septembre 2026.", line.Message);

        line.Copy.Execute(null);
        Assert.Equal(line.Message, fake.Clipboard);
        line.WhatsApp.Execute(null);
        Assert.StartsWith("https://wa.me/213", Assert.Single(fake.OpenedUrls));

        await dialog.ConfirmCommand.ExecuteAsync(null); // "Copier tous les messages"
        await running;
        Assert.Contains(dialog.Lines[0].Message, fake.Clipboard);
        Assert.Contains(dialog.Lines[1].Message, fake.Clipboard);
        Assert.Null(host.Get<DialogHost>().Current);

        // Reopening the session keeps the saved absences available.
        await ((IAsyncRelayCommand)todo.Select).ExecuteAsync(null);
        Assert.Equal(2, page.SavedAbsentCount);
    }

    [Fact]
    public async Task Absence_notices_open_automatically_when_enabled_in_settings()
    {
        await using var host = await UiHost.CreateAsync();
        var settings = host.Get<ISettingsService>();
        var s = await settings.GetAsync();
        s.NotifyParentOnAbsence = true;
        await settings.SaveAsync(s);

        await host.Get<Navigator>().NavigateAsync<AttendanceViewModel>();
        var page = host.Page<AttendanceViewModel>();
        var todo = page.SessionItems.First(i => i.Progress == "À faire");
        await ((IAsyncRelayCommand)todo.Select).ExecuteAsync(null);

        // All present: nothing to tell.
        page.AllPresentCommand.Execute(null);
        await page.SaveCommand.ExecuteAsync(null);
        Assert.Null(host.Get<DialogHost>().Current);

        page.Rows[0].Status = AttendanceStatus.Absent;
        var running = page.SaveCommand.ExecuteAsync(null);
        var dialog = await WaitForDialogAsync(host, running);
        Assert.Equal(page.Rows[0].Name, Assert.Single(dialog.Lines).Student);
        dialog.Cancel();
        await running;
        Assert.False(page.HasError, page.Error);
    }

    private static async Task<AbsenceNoticeDialogViewModel> WaitForDialogAsync(TestHost host, Task running)
    {
        var dialogs = host.Get<DialogHost>();
        for (var i = 0; i < 300 && dialogs.Current is not AbsenceNoticeDialogViewModel && !running.IsCompleted; i++) await Task.Delay(10);
        return Assert.IsType<AbsenceNoticeDialogViewModel>(dialogs.Current);
    }

    [Fact]
    public async Task Clicking_select_all_on_a_partial_selection_selects_everything()
    {
        await using var host = await UiHost.CreateAsync();
        var page = await OpenRemindersAsync(host);
        Assert.True(page.Reminders.Count > 1);
        page.Reminders[0].IsSelected = false;
        Assert.Null(page.AllRemindersSelected);
        page.AllRemindersSelected = false; // what a two-state CheckBox does when clicked in the partial state
        Assert.True(page.AllRemindersSelected);
        Assert.All(page.Reminders, r => Assert.True(r.IsSelected));
        page.AllRemindersSelected = false; // second click clears
        Assert.All(page.Reminders, r => Assert.False(r.IsSelected));
    }
}
