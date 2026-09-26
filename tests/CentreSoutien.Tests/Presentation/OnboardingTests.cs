using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CentreSoutien.Presentation.ViewModels.Pages;
using CentreSoutien.Presentation.ViewModels.Shell;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Tests.Presentation;

public class OnboardingTests
{
    private static Task Run(IRelayCommand command) => command is IAsyncRelayCommand a ? a.ExecuteAsync(null) : Sync(command);

    private static Task Sync(IRelayCommand command)
    {
        command.Execute(null);
        return Task.CompletedTask;
    }

    private static async Task<DashboardViewModel> OpenAsync(TestHost host)
    {
        await host.Get<Navigator>().NavigateAsync<DashboardViewModel>();
        var page = host.Page<DashboardViewModel>();
        Assert.False(page.HasError, page.Error);
        return page;
    }

    /// <summary>Runs a command that opens a dialog and returns the dialog with the still running command.</summary>
    private static async Task<(T Dialog, Task Running)> OpenDialogAsync<T>(TestHost host, Func<Task> command) where T : DialogViewModel
    {
        var running = command();
        var dialogs = host.Get<DialogHost>();
        for (var i = 0; i < 200 && dialogs.Current is not T && !running.IsCompleted; i++) await Task.Delay(10);
        return (Assert.IsType<T>(dialogs.Current), running);
    }

    private static OnboardingStepRow Step(DashboardViewModel page, OnboardingStep step) => page.OnboardingSteps.Single(s => s.Step == step);

    [Fact]
    public async Task Fresh_database_shows_every_step_to_do_and_offers_demo_data()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var page = await OpenAsync(host);

        Assert.True(page.ShowOnboarding);
        Assert.Equal(9, page.OnboardingSteps.Count);
        Assert.All(page.OnboardingSteps, s => Assert.False(s.IsDone, s.Title));
        Assert.Equal("0 / 9 étapes", page.OnboardingProgress);
        Assert.Equal(0, page.OnboardingDoneShare);
        Assert.True(Step(page, OnboardingStep.Logo).IsOptional);
        Assert.Contains("Excel", Step(page, OnboardingStep.Students).Hint);
        Assert.True(page.CanLoadDemo);

        // "Essayer avec des données de démonstration": confirm, then the dashboard reloads with the data.
        var (confirm, running) = await OpenDialogAsync<ConfirmDialogViewModel>(host, () => page.LoadDemoCommand.ExecuteAsync(null));
        await confirm.ConfirmCommand.ExecuteAsync(null);
        await running;
        Assert.False(page.HasError, page.Error);
        Assert.False(page.CanLoadDemo);
        Assert.True(Step(page, OnboardingStep.Students).IsDone);
        Assert.False(await host.Get<IDemoDataService>().IsDatabaseEmptyAsync());

        // The settings shortcut is kept.
        await page.GoSettingsCommand.ExecuteAsync(null);
        Assert.True(host.Page<SettingsViewModel>().IsBackup);
    }

    [Fact]
    public async Task Demo_data_completes_most_steps_and_the_checklist_hides_when_setup_is_complete()
    {
        await using var host = await UiHost.CreateAsync();
        var page = await OpenAsync(host);

        Assert.False(page.CanLoadDemo);
        foreach (var done in new[] { OnboardingStep.Subjects, OnboardingStep.Rooms, OnboardingStep.Teachers, OnboardingStep.CoursesAndGroups, OnboardingStep.Students })
            Assert.True(Step(page, done).IsDone, done.ToString());
        Assert.False(Step(page, OnboardingStep.Backup).IsDone);
        Assert.True(page.ShowOnboarding);
        var doneCount = page.OnboardingSteps.Count(s => s.IsDone);
        Assert.Equal($"{doneCount} / 9 étapes", page.OnboardingProgress);
        Assert.Equal(doneCount / 9.0, page.OnboardingDoneShare, 3);

        // Finish every required step (the logo is optional).
        var factory = host.Get<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var s = await db.Settings.FirstAsync();
            s.Address = "12 rue Didouche Mourad, Alger";
            s.Phone = "021 00 00 00";
            s.LogoFile = null;
            s.LastBackupAt = host.Clock.GetLocalNow().DateTime;
            (await db.Accounts.FirstAsync()).MustChangePassword = false;
            await db.SaveChangesAsync();
        }
        page = await OpenAsync(host);
        Assert.All(page.OnboardingSteps.Where(s => !s.IsOptional), s => Assert.True(s.IsDone, s.Title));
        Assert.False(Step(page, OnboardingStep.Logo).IsDone);
        Assert.False(page.ShowOnboarding);
    }

    [Fact]
    public async Task Masquer_persists_on_this_computer()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var page = await OpenAsync(host);
        Assert.True(page.ShowOnboarding);

        page.DismissOnboardingCommand.Execute(null);
        Assert.False(page.ShowOnboarding);
        Assert.True(host.Get<IUserPreferences>().Get(PreferenceKeys.OnboardingDismissed, false));

        page = await OpenAsync(host);
        Assert.False(page.ShowOnboarding);
        Assert.NotEmpty(page.OnboardingSteps);
    }

    [Theory]
    [InlineData(OnboardingStep.CenterInfo, typeof(SettingsViewModel))]
    [InlineData(OnboardingStep.Logo, typeof(SettingsViewModel))]
    [InlineData(OnboardingStep.Password, typeof(AccountViewModel))]
    [InlineData(OnboardingStep.Subjects, typeof(SubjectsViewModel))]
    [InlineData(OnboardingStep.Rooms, typeof(RoomsViewModel))]
    [InlineData(OnboardingStep.Teachers, typeof(TeachersViewModel))]
    [InlineData(OnboardingStep.CoursesAndGroups, typeof(GroupsViewModel))]
    [InlineData(OnboardingStep.Students, typeof(StudentsViewModel))]
    [InlineData(OnboardingStep.Backup, typeof(SettingsViewModel))]
    public async Task Each_step_opens_the_right_page(OnboardingStep step, Type expected)
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var page = await OpenAsync(host);
        await Run(Step(page, step).Do);
        var current = host.Get<Navigator>().Current;
        Assert.IsType(expected, current);
        Assert.False(current!.HasError, current.Error);
        if (step == OnboardingStep.Backup) Assert.True(((SettingsViewModel)current).IsBackup);
        if (step == OnboardingStep.CenterInfo) Assert.True(((SettingsViewModel)current).IsCentre);
    }

    [Fact]
    public async Task Greeting_and_alert_count_are_shown()
    {
        Assert.Equal("Bonjour Mourad — samedi 26 septembre 2026", DashboardViewModel.GreetingFor(" Mourad  Benali", "Samedi 26 septembre 2026"));
        Assert.Equal("Samedi 26 septembre 2026", DashboardViewModel.GreetingFor("", "Samedi 26 septembre 2026"));
        Assert.Equal("Samedi 26 septembre 2026", DashboardViewModel.GreetingFor(null, "Samedi 26 septembre 2026"));

        await using var host = await UiHost.CreateAsync();
        host.Get<AppSession>().Account!.FullName = "Mourad Benali";
        var page = await OpenAsync(host);
        Assert.StartsWith("Bonjour Mourad — ", page.Greeting);
        Assert.EndsWith(page.DateLabel[1..], page.Greeting);
        Assert.Equal($"À traiter ({page.Alerts.Count})", page.AlertsTitle);
        Assert.Equal($"{page.Alerts.Count} points", page.AlertCount);
    }

    [Fact]
    public async Task Quick_actions_open_the_right_dialog_or_page()
    {
        await using var host = await UiHost.CreateAsync();
        var page = await OpenAsync(host);
        Assert.Equal(["collect", "student", "attendance", "expense", "reminders", "search"], page.QuickActions.Select(a => a.Key));
        Assert.All(page.QuickActions, a => Assert.False(string.IsNullOrEmpty(a.Glyph + a.Hint)));
        QuickAction Action(string key) => page.QuickActions.Single(a => a.Key == key);

        // Encaisser un paiement: the existing dialog, with a student picker.
        var (collect, running) = await OpenDialogAsync<CollectPaymentDialogViewModel>(host, () => Run(Action("collect").Command));
        Assert.True(collect.CanPickStudent);
        collect.Cancel();
        await running;

        // Inscrire un élève: then the new profile opens.
        var (student, adding) = await OpenDialogAsync<StudentEditorDialogViewModel>(host, () => Run(Action("student").Command));
        student.FirstName = "Lina";
        student.LastName = "Zeroual";
        student.Level = "2AS";
        await student.ConfirmCommand.ExecuteAsync(null);
        await adding;
        Assert.False(student.HasError, student.Error);
        Assert.Equal(student.SavedId, host.Page<StudentDetailViewModel>().Id);

        // Faire l'appel: today's attendance.
        page = await OpenAsync(host);
        await Run(page.QuickActions.Single(a => a.Key == "attendance").Command);
        var attendance = host.Page<AttendanceViewModel>();
        Assert.False(attendance.HasError, attendance.Error);
        Assert.Equal(host.Clock.GetLocalNow().Date, attendance.Day);

        // Ajouter une dépense: the expense editor, dated today; the dashboard reloads afterwards.
        page = await OpenAsync(host);
        var (expense, saving) = await OpenDialogAsync<ExpenseEditorDialogViewModel>(host, () => Run(page.QuickActions.Single(a => a.Key == "expense").Command));
        Assert.Equal(host.Clock.GetLocalNow().Date, expense.Date);
        expense.Description = "Craies";
        expense.Amount = "1500";
        await expense.ConfirmCommand.ExecuteAsync(null);
        await saving;
        Assert.False(expense.HasError, expense.Error);
        Assert.Same(page, host.Get<Navigator>().Current);

        // Relancer les impayés: Paiements, on the reminders tab.
        await Run(page.QuickActions.Single(a => a.Key == "reminders").Command);
        var payments = host.Page<PaymentsViewModel>();
        Assert.True(payments.IsReminders);
        Assert.Equal("Septembre 2026", payments.MonthLabel);
        Assert.NotEmpty(payments.Reminders);

        // Rechercher: the global search (Ctrl+K).
        page = await OpenAsync(host);
        await Run(page.QuickActions.Single(a => a.Key == "search").Command);
        Assert.True(host.Get<SearchViewModel>().IsOpen);
    }

    [Fact]
    public async Task Payments_target_keeps_the_month_parameter_working()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<PaymentsViewModel>(new PaymentsViewModel.Target(new DateTime(2026, 8, 1), "receipts"));
        var page = host.Page<PaymentsViewModel>();
        Assert.Equal("Août 2026", page.MonthLabel);
        Assert.True(page.IsReceipts);

        await host.Get<Navigator>().NavigateAsync<PaymentsViewModel>(new DateTime(2026, 8, 1));
        page = host.Page<PaymentsViewModel>();
        Assert.Equal("Août 2026", page.MonthLabel);
        Assert.True(page.IsMonthly);
    }
}

public class CollectPaymentQuickAmountsTests
{
    private static async Task<(CollectPaymentDialogViewModel Dialog, PaymentRow Row)> OpenAsync(TestHost host, Func<PaymentRow, bool> pick)
    {
        var row = (await host.Get<IPaymentService>().OverviewAsync()).First(pick);
        var dialog = host.Get<CollectPaymentDialogViewModel>();
        await dialog.InitializeAsync(row.StudentId);
        return (dialog, row);
    }

    [Fact]
    public async Task Chips_fill_the_amount_and_the_remaining_balance_follows_the_typing()
    {
        await using var host = await UiHost.CreateAsync();
        var (dialog, row) = await OpenAsync(host, r => r.Balance > 0 && r.Paid == 0 && r.Due > 0);
        var fee = (await host.Get<ISettingsService>().GetAsync()).RegistrationFee;

        Assert.Equal($"reste {Money.Format(row.Balance)}", dialog.StudentBalance);
        Assert.True(dialog.HasQuickAmounts);
        Assert.Equal("Reste à payer", dialog.QuickAmounts[0].Label);
        Assert.Equal("Frais d'inscription", dialog.QuickAmounts[^1].Label);
        // Suggested amount = the balance, so nothing is left.
        Assert.Equal(Money.Number(row.Balance), dialog.Amount);
        Assert.Equal($"Reste après ce paiement : {Money.Format(0)}", dialog.RemainingAfter);
        Assert.Contains("séance", dialog.Summary); // groups and where the student is in the pack

        dialog.Amount = "1000";
        Assert.Equal($"Reste après ce paiement : {Money.Format(row.Balance - 1000)}", dialog.RemainingAfter);

        dialog.Amount = Money.Number(row.Balance + 500);
        Assert.Contains("500 DZD payés d'avance", dialog.RemainingAfter);

        dialog.QuickAmounts.Single(q => q.Label == "Frais d'inscription").Apply.Execute(null);
        Assert.Equal(PaymentKind.Registration, dialog.SelectedKind!.Value);
        Assert.Equal(Money.Number(fee), dialog.Amount);
        Assert.False(dialog.HasRemainingAfter);

        dialog.QuickAmounts.Single(q => q.Label == "Reste à payer").Apply.Execute(null);
        Assert.Equal(PaymentKind.Sessions, dialog.SelectedKind!.Value);
        Assert.Equal(Money.Number(row.Balance), dialog.Amount);

        // The receipt is still printed.
        await dialog.ConfirmCommand.ExecuteAsync(null);
        Assert.False(dialog.HasError, dialog.Error);
        Assert.Contains(dialog.Result!.ReceiptNumber, host.Get<FakePlatform>().Printed);
    }

    [Fact]
    public async Task Paid_up_student_has_no_balance_chip_and_the_picker_shows_balances()
    {
        await using var host = await UiHost.CreateAsync();
        var (dialog, row) = await OpenAsync(host, r => r.Balance <= 0 && r.Due > 0);
        Assert.Equal("à jour", dialog.StudentBalance);
        Assert.DoesNotContain(dialog.QuickAmounts, q => q.Label == "Reste à payer");
        // Up to date: the next pack is proposed (paid in advance).
        Assert.Contains(dialog.QuickAmounts, q => q.Label == "Prochaines séances" && q.Value == row.PackPrice);
        Assert.Equal(Money.Number(row.PackPrice), dialog.Amount);

        var picker = host.Get<CollectPaymentDialogViewModel>();
        await picker.InitializeAsync(null);
        Assert.True(picker.CanPickStudent);
        Assert.Empty(picker.QuickAmounts);
        Assert.False(picker.HasRemainingAfter);
        Assert.Contains(picker.Students, s => s.Label.Contains("— reste "));
        picker.SelectedStudent = picker.Students.First(s => s.Label.Contains("— reste "));
        Assert.NotEmpty(picker.QuickAmounts);
        Assert.True(picker.HasRemainingAfter);
    }
}
