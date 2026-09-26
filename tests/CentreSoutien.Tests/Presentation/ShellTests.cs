using CentreSoutien.Application.Abstractions;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CentreSoutien.Presentation.ViewModels.Pages;
using CentreSoutien.Presentation.ViewModels.Shell;

namespace CentreSoutien.Tests.Presentation;

public class ShellTests
{
    [Fact]
    public async Task Sidebar_has_exactly_the_requested_modules_and_no_user_management()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var shell = host.Get<ShellViewModel>();
        var labels = shell.NavGroups.SelectMany(g => g.Items).Select(i => i.Label).ToList();
        Assert.Equal(
            ["Tableau de bord", "Élèves", "Parents", "Enseignants", "Matières", "Groupes", "Salles", "Emploi du temps", "Séances", "Présences",
             "Notes", "Examens", "Paiements", "Paiements enseignants", "Dépenses", "Rapports", "Documents", "Paramètres"], labels);
        Assert.DoesNotContain(labels, l => l.Contains("Utilisateur") || l.Contains("Rôle") || l.Contains("Permission"));
    }

    [Fact]
    public async Task Every_sidebar_page_loads_with_demo_data_without_error()
    {
        await using var host = await UiHost.CreateAsync();
        var shell = host.Get<ShellViewModel>();
        var nav = host.Get<Navigator>();
        foreach (var item in shell.NavGroups.SelectMany(g => g.Items))
        {
            await item.GoCommand.ExecuteAsync(null);
            Assert.NotNull(nav.Current);
            Assert.Equal(item.Page, nav.Current!.GetType());
            Assert.True(item.IsActive);
            Assert.False(nav.Current.HasError, $"{item.Label}: {nav.Current.Error}");
        }
        await nav.NavigateAsync<AccountViewModel>();
        Assert.False(nav.Current!.HasError, nav.Current.Error);
    }

    [Fact]
    public async Task Login_flow_forces_password_change_then_locks_and_unlocks()
    {
        await using var host = await TestHost.CreateAsync(configure: sc => sc.AddPresentationWithFakes());
        var shell = host.Get<ShellViewModel>();
        var session = host.Get<AppSession>();
        await shell.InitializeAsync();
        Assert.True(shell.ShowLogin);
        Assert.True(shell.Login.IsFirstRun);
        Assert.Equal("admin", shell.Login.Username);

        shell.Login.Password = "wrong";
        await shell.Login.SignInCommand.ExecuteAsync(null);
        Assert.True(shell.Login.HasError);
        Assert.True(shell.ShowLogin);

        shell.Login.Password = "admin";
        await shell.Login.SignInCommand.ExecuteAsync(null);
        Assert.True(shell.ShowChangePassword);
        Assert.False(shell.ShowApp);

        shell.ChangePassword.CurrentPassword = "admin";
        shell.ChangePassword.NewPassword = "Centre2026!";
        shell.ChangePassword.ConfirmPassword = "Centre2026!";
        await shell.ChangePassword.SaveCommand.ExecuteAsync(null);
        Assert.False(shell.ChangePassword.HasError, shell.ChangePassword.Error);
        Assert.True(shell.ShowApp);
        Assert.IsType<DashboardViewModel>(host.Get<Navigator>().Current);

        Assert.True(shell.IsAppInteractive);
        shell.LockNowCommand.Execute(null);
        Assert.True(shell.ShowLock);
        Assert.False(shell.IsAppInteractive); // nothing behind the lock screen can take focus
        shell.Lock.Password = "admin";
        await shell.Lock.UnlockCommand.ExecuteAsync(null);
        Assert.True(shell.ShowLock);
        shell.Lock.Password = "Centre2026!";
        await shell.Lock.UnlockCommand.ExecuteAsync(null);
        Assert.False(shell.ShowLock);

        var signOut = shell.SignOutCommand.ExecuteAsync(null);
        Assert.False(shell.IsAppInteractive); // confirmation dialog open
        await host.AnswerDialogAsync();
        await signOut;
        Assert.True(shell.ShowLogin);
        Assert.Null(host.Get<Navigator>().Current);
        Assert.Equal(SessionState.SignedOut, session.State);
    }

    [Fact]
    public async Task Owner_collects_a_payment_from_the_student_profile()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<StudentsViewModel>();
        var list = host.Page<StudentsViewModel>();
        var unpaid = list.Rows.First(r => r.BalanceValue > 0);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)unpaid.Open).ExecuteAsync(null);

        var profile = host.Page<StudentDetailViewModel>();
        Assert.Equal(unpaid.Name, profile.Name);
        var task = profile.AddPaymentCommand.ExecuteAsync(null);
        var dialog = (CollectPaymentDialogViewModel)(await host.AnswerDialogAsync())!;
        await task;
        Assert.False(dialog.HasError, dialog.Error);
        Assert.NotNull(dialog.Result);
        Assert.Contains(dialog.Result!.ReceiptNumber, host.Get<FakePlatform>().Printed);
        Assert.Equal("Payé", profile.State.Text);
        Assert.Contains(profile.Payments, p => p.Receipt == dialog.Result.ReceiptNumber);
    }

    [Fact]
    public async Task Owner_adds_a_student_with_a_new_parent_and_enrolls_them()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<StudentsViewModel>();
        var add = host.Page<StudentsViewModel>().AddCommand.ExecuteAsync(null);
        var editor = (StudentEditorDialogViewModel)host.Get<DialogHost>().Current!;
        editor.FirstName = "Nour";
        editor.LastName = "Test";
        editor.Level = "3AS";
        editor.CreateParent = true;
        editor.NewParentName = "M. Test";
        editor.NewParentPhone = "0550 00 00 00";
        await editor.ConfirmCommand.ExecuteAsync(null);
        await add;
        Assert.False(editor.HasError, editor.Error);

        var profile = host.Page<StudentDetailViewModel>();
        Assert.Equal("Nour Test", profile.Name);
        Assert.Contains("M. Test", profile.ParentName);

        var enroll = profile.EnrollCommand.ExecuteAsync(null);
        var dialog = (EnrollDialogViewModel)host.Get<DialogHost>().Current!;
        Assert.NotEmpty(dialog.GroupOptions);
        await dialog.ConfirmCommand.ExecuteAsync(null);
        await enroll;
        Assert.Single(profile.Enrollments);
    }

    [Fact]
    public async Task Student_editor_refuses_an_invalid_or_future_birth_date()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var editor = host.Get<StudentEditorDialogViewModel>();
        await editor.InitializeAsync(null);
        editor.FirstName = "Lina";
        editor.LastName = "Kaci";
        editor.Level = "4AM";

        editor.BirthDateInvalid = true; // what the date field reports for "31/02/2010"
        await editor.ConfirmCommand.ExecuteAsync(null);
        Assert.Contains("Date de naissance invalide", editor.Error);
        Assert.Null(editor.SavedId);

        editor.BirthDateInvalid = false;
        editor.BirthDate = new DateTime(2030, 1, 1);
        await editor.ConfirmCommand.ExecuteAsync(null);
        Assert.Contains("futur", editor.Error);

        editor.BirthDate = new DateTime(2011, 3, 15);
        await editor.ConfirmCommand.ExecuteAsync(null);
        Assert.False(editor.HasError, editor.Error);
        var saved = await host.Get<CentreSoutien.Application.Abstractions.IStudentService>().GetAsync(editor.SavedId!.Value);
        Assert.Equal(new DateTime(2011, 3, 15), saved!.BirthDate);
    }
}
