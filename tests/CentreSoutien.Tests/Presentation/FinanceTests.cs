using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CentreSoutien.Presentation.ViewModels.Pages;
using CentreSoutien.Presentation.ViewModels.Shell;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Tests.Presentation;

public class FinanceTests
{
    /// <summary>Runs a command that opens a dialog, lets <paramref name="fill"/> edit it, confirms (or cancels) it and waits for the command.</summary>
    private static async Task<T> WithDialogAsync<T>(TestHost host, Func<Task> command, Action<T>? fill = null, bool confirm = true) where T : DialogViewModel
    {
        var running = command();
        var dialogs = host.Get<DialogHost>();
        for (var i = 0; i < 200 && dialogs.Current is not T && !running.IsCompleted; i++) await Task.Delay(10);
        var dialog = Assert.IsType<T>(dialogs.Current);
        fill?.Invoke(dialog);
        if (confirm) await dialog.ConfirmCommand.ExecuteAsync(null);
        else dialog.Cancel();
        await running;
        return dialog;
    }

    private static Task Run(IRelayCommand command) => ((IAsyncRelayCommand)command).ExecuteAsync(null);

    [Fact]
    public async Task Payments_page_shows_month_kpis_rows_and_receipts()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<PaymentsViewModel>();
        var page = host.Page<PaymentsViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.Equal("Septembre 2026", page.MonthLabel);
        Assert.Equal(["Attendu ce mois", "Encaissé", "Reste à percevoir", "Taux de recouvrement"], page.Kpis.Select(k => k.Label));
        Assert.NotEmpty(page.Rows);
        Assert.NotEmpty(page.LatestReceipts);
        Assert.True(page.LatestReceipts.Count <= 12);
        Assert.NotEmpty(page.Receipts);

        page.SelectedFilter = "Impayé";
        Assert.All(page.Rows, r => Assert.Equal("Impayé", r.State.Text));
        page.SelectedFilter = "Tous";
        var name = page.Rows[0].Name;
        page.SearchText = name;
        Assert.Contains(page.Rows, r => r.Name == name);
    }

    [Fact]
    public async Task Collecting_from_a_row_prints_the_receipt_and_updates_the_row()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<PaymentsViewModel>();
        var page = host.Page<PaymentsViewModel>();
        var row = page.Rows.First(r => r.CanCollect);

        var dialog = await WithDialogAsync<CollectPaymentDialogViewModel>(host, () => Run(row.Collect));
        Assert.False(dialog.HasError, dialog.Error);
        Assert.NotNull(dialog.Result);
        Assert.Contains(dialog.Result!.ReceiptNumber, host.Get<FakePlatform>().Printed);

        var updated = page.Rows.First(r => r.StudentId == row.StudentId);
        Assert.False(updated.CanCollect);
        Assert.Equal("Payé", updated.State.Text);
        Assert.Contains(page.Receipts, r => r.Number == dialog.Result.ReceiptNumber);

        // Reprint from the "Derniers reçus" column.
        host.Get<FakePlatform>().Printed.Clear();
        await Run(page.LatestReceipts[0].Print);
        Assert.Single(host.Get<FakePlatform>().Printed);
    }

    [Fact]
    public async Task Discounts_can_be_created_edited_and_deleted()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<PaymentsViewModel>();
        var page = host.Page<PaymentsViewModel>();

        await WithDialogAsync<DiscountEditorDialogViewModel>(host, () => page.AddDiscountCommand.ExecuteAsync(null), d =>
        {
            d.Name = "Enfant du personnel";
            d.SelectedType = d.Types.First(t => t.Value == DiscountType.FixedAmount);
            d.Value = "500";
        });
        var line = Assert.Single(page.DiscountLines, l => l.Name == "Enfant du personnel");
        Assert.Equal("Montant fixe", line.Type);

        var edit = await WithDialogAsync<DiscountEditorDialogViewModel>(host, () => Run(line.Edit), d =>
        {
            Assert.Equal("500", d.Value);
            d.Value = "750";
            d.IsActive = false;
        });
        Assert.False(edit.HasError, edit.Error);
        line = Assert.Single(page.DiscountLines, l => l.Name == "Enfant du personnel");
        Assert.Contains("750", line.Value);
        Assert.Equal("Inactif", line.Active.Text);

        await WithDialogAsync<ConfirmDialogViewModel>(host, () => Run(line.Delete));
        Assert.DoesNotContain(page.DiscountLines, l => l.Name == "Enfant du personnel");
    }

    [Fact]
    public async Task Teacher_payments_page_loads_and_records_a_payment()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<TeacherPaymentsViewModel>();
        var page = host.Page<TeacherPaymentsViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.Equal(["Total dû", "Versé", "Reste à verser"], page.Kpis.Select(k => k.Label));
        var line = page.Lines.First(l => l.CanPay);
        var before = page.History.Count;

        var dialog = await WithDialogAsync<TeacherPaymentDialogViewModel>(host, () => Run(line.Pay));
        Assert.False(dialog.HasError, dialog.Error);
        var updated = page.Lines.First(l => l.TeacherId == line.TeacherId);
        Assert.False(updated.CanPay);
        Assert.Equal(before + 1, page.History.Count);

        var entry = page.History.First(h => h.Teacher == line.Name);
        await WithDialogAsync<ConfirmDialogViewModel>(host, () => Run(entry.Delete));
        Assert.Equal(before, page.History.Count);
        Assert.True(page.Lines.First(l => l.TeacherId == line.TeacherId).CanPay);
    }

    [Fact]
    public async Task Expenses_can_be_added_edited_and_deleted()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<ExpensesViewModel>();
        var page = host.Page<ExpensesViewModel>();
        Assert.False(page.HasError, page.Error);
        var count = page.Rows.Count;

        await WithDialogAsync<ExpenseEditorDialogViewModel>(host, () => page.AddCommand.ExecuteAsync(null), d =>
        {
            d.Category = "Fournitures";
            d.Description = "Feutres pour tableau";
            d.Supplier = "Papeterie El Feth";
            d.Amount = "2 350";
        });
        Assert.Equal(count + 1, page.Rows.Count);
        var line = page.Rows.First(r => r.Description == "Feutres pour tableau");
        Assert.Equal(2350m, line.AmountValue);
        Assert.Contains("Fournitures", page.Categories);

        page.SelectedCategory = "Fournitures";
        Assert.All(page.Rows, r => Assert.Equal("Fournitures", r.Category));
        page.SelectedCategory = "Toutes";

        await WithDialogAsync<ExpenseEditorDialogViewModel>(host, () => Run(line.Edit), d => d.Amount = "2500");
        line = page.Rows.First(r => r.Description == "Feutres pour tableau");
        Assert.Equal(2500m, line.AmountValue);

        await WithDialogAsync<ConfirmDialogViewModel>(host, () => Run(line.Delete));
        Assert.Equal(count, page.Rows.Count);
    }

    [Fact]
    public async Task Reports_load_print_and_export_to_excel()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<ReportsViewModel>();
        var page = host.Page<ReportsViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.Equal(4, page.Summary.Count);
        Assert.Equal("Résultat", page.SummaryCosts[^1].Label);
        Assert.NotEmpty(page.ByCourse);
        Assert.NotEmpty(page.Teachers);

        await page.PrintCommand.ExecuteAsync(null);
        Assert.False(page.HasError, page.Error);
        Assert.Contains(host.Get<FakePlatform>().Printed, p => p.Contains("Septembre 2026"));

        var path = Path.Combine(host.Folder, "export-test.xlsx");
        host.Get<FakePlatform>().NextSaveFile = path;
        await page.ExportAllCommand.ExecuteAsync(null);
        Assert.False(page.HasError, page.Error);
        Assert.True(File.Exists(path));
        Assert.Contains(path, host.Get<FakePlatform>().Opened);
    }

    [Fact]
    public async Task Documents_can_be_added_listed_and_deleted()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<DocumentsViewModel>();
        var page = host.Page<DocumentsViewModel>();
        Assert.False(page.HasError, page.Error);
        var count = page.Rows.Count;

        var file = Path.Combine(host.Folder, "reglement-interieur.pdf");
        await File.WriteAllTextAsync(file, "%PDF-1.4 test");
        host.Get<FakePlatform>().NextOpenFile = file;
        await WithDialogAsync<AddDocumentDialogViewModel>(host, () => page.AddCommand.ExecuteAsync(null), d => d.Category = "Contrat");
        Assert.Equal(count + 1, page.Rows.Count);
        var line = page.Rows.First(r => r.Title == "reglement-interieur");
        Assert.Equal("Centre", line.Owner);
        Assert.False(line.HasOwnerPage);

        page.SelectedFilter = page.Filters.First(f => f.Value == DocumentOwnerType.Student);
        Assert.DoesNotContain(page.Rows, r => r.Id == line.Id);
        page.SelectedFilter = page.Filters[0];

        line.Open.Execute(null);
        Assert.Contains(host.Get<FakePlatform>().Opened, p => p.EndsWith(".pdf"));

        await WithDialogAsync<ConfirmDialogViewModel>(host, () => Run(line.Delete));
        Assert.Equal(count, page.Rows.Count);
    }

    [Fact]
    public async Task Settings_save_updates_the_shell_and_the_session()
    {
        await using var host = await UiHost.CreateAsync();
        var shell = host.Get<ShellViewModel>();
        await host.Get<Navigator>().NavigateAsync<SettingsViewModel>();
        var page = host.Page<SettingsViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.Equal(8, page.Sections.Count);

        page.SelectedSection = page.Sections.First(s => s.Value == "sec");
        Assert.True(page.IsSecurity);
        page.Form.CenterName = "Centre Excellence";
        page.SelectedLockDelay = page.LockDelays.First(o => o.Value == 30);
        page.SelectedSessionTimeout = page.SessionTimeouts.First(o => o.Value == 60);
        page.AutoBackupTime = "21h30";
        await page.SaveCommand.ExecuteAsync(null);
        Assert.False(page.HasError, page.Error);

        Assert.Equal("Centre Excellence", shell.CenterName);
        var session = host.Get<AppSession>();
        Assert.Equal(30, session.AutoLockMinutes);
        Assert.Equal(60, session.SessionTimeoutMinutes);
        var saved = await host.Get<ISettingsService>().GetAsync();
        Assert.Equal("Centre Excellence", saved.CenterName);
        Assert.Equal(new TimeSpan(21, 30, 0), saved.AutoBackupTime);

        page.SelectedTheme = page.Themes.First(t => t.Value == AppTheme.Dark);
        Assert.Equal(AppTheme.Dark, host.Get<FakePlatform>().Current);
        Assert.True(shell.IsDark);
    }

    [Fact]
    public async Task Backup_now_creates_a_file_and_restore_signs_the_owner_out()
    {
        await using var host = await UiHost.CreateAsync();
        Assert.True((await host.Get<IAuthService>().LoginAsync("admin", "admin")).Succeeded); // wraps the key for backups
        await host.Get<Navigator>().NavigateAsync<SettingsViewModel>();
        var page = host.Page<SettingsViewModel>();
        var platform = host.Get<FakePlatform>();

        var folder = Path.Combine(host.Folder, "mes-sauvegardes");
        platform.NextFolder = folder;
        page.BrowseBackupFolderCommand.Execute(null);
        Assert.Equal(folder, page.BackupFolder);
        await page.BackupNowCommand.ExecuteAsync(null);
        Assert.False(page.HasError, page.Error);
        Assert.NotNull(page.LastBackupFile);
        Assert.True(File.Exists(page.LastBackupFile));
        Assert.StartsWith(folder, page.LastBackupFile);
        Assert.NotEqual("Jamais", page.LastBackupLabel);

        platform.NextOpenFile = page.LastBackupFile;
        var dialog = await WithDialogAsync<RestoreBackupDialogViewModel>(host, () => page.RestoreCommand.ExecuteAsync(null), d =>
        {
            Assert.Equal(page.LastBackupFile, d.FilePath);
            d.Password = "admin";
        });
        Assert.False(dialog.HasError, dialog.Error);
        Assert.Equal(SessionState.SignedOut, host.Get<AppSession>().State);
    }

    [Fact]
    public async Task Owner_changes_username_and_password_from_the_account_page()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<AccountViewModel>();
        var page = host.Page<AccountViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.Equal("admin", page.Username);
        Assert.StartsWith("Connecté depuis", page.SessionLabel);

        page.Username = "directeur";
        page.FullName = "Mourad Benyahia";
        await page.SaveProfileCommand.ExecuteAsync(null);
        Assert.False(page.HasError, page.Error);
        Assert.Equal("MB", host.Get<ShellViewModel>().OwnerInitials);

        page.CurrentPassword = "admin";
        page.NewPassword = "Centre2026!";
        page.ConfirmPassword = "Autre2026!";
        await page.ChangePasswordCommand.ExecuteAsync(null);
        Assert.True(page.HasPasswordError);

        page.ConfirmPassword = "Centre2026!";
        await page.ChangePasswordCommand.ExecuteAsync(null);
        Assert.False(page.HasPasswordError, page.PasswordError);
        Assert.Equal("", page.CurrentPassword);
        Assert.Equal("", page.NewPassword);

        var auth = host.Get<IAuthService>();
        Assert.True((await auth.LoginAsync("directeur", "Centre2026!")).Succeeded);
        Assert.False((await auth.LoginAsync("admin", "admin")).Succeeded);
    }
}
