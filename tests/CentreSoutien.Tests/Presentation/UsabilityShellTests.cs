using CentreSoutien.Application.Abstractions;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CentreSoutien.Presentation.ViewModels.Pages;
using CentreSoutien.Presentation.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Tests.Presentation;

/// <summary>Sidebar icons, page help, keyboard shortcuts, text size and header back button.</summary>
public class UsabilityShellTests
{
    private static readonly string[] AllNavKeys =
    [
        "dashboard", "students", "parents", "teachers", "subjects", "courses", "groups", "rooms", "schedule", "sessions",
        "attendance", "grades", "exams", "payments", "tpayments", "expenses", "reports", "documents", "settings", "account",
    ];

    [Fact]
    public async Task Every_page_has_help_text_and_every_sidebar_entry_an_icon()
    {
        foreach (var key in AllNavKeys)
        {
            var help = PageHelp.For(key);
            Assert.NotNull(help);
            Assert.False(string.IsNullOrWhiteSpace(help!.Title), key);
            Assert.True(help.Purpose.Length > 40, key);
            Assert.InRange(help.Steps.Count, 2, 6);
        }

        await using var host = await UiHost.CreateAsync(demo: false);
        var shell = host.Get<ShellViewModel>();
        var items = shell.NavGroups.SelectMany(g => g.Items).ToList();
        Assert.All(items, i => Assert.False(string.IsNullOrEmpty(i.Glyph), i.Label));
        Assert.All(items, i => Assert.NotNull(PageHelp.For(i.Key)));
        // The page's own NavKey drives the help shown by the shell.
        var nav = host.Get<Navigator>();
        foreach (var item in items)
        {
            await item.GoCommand.ExecuteAsync(null);
            Assert.Equal(PageHelp.For(nav.Current!.NavKey), shell.CurrentHelp);
        }
        await nav.NavigateAsync<AccountViewModel>();
        Assert.Equal("Mon compte", shell.CurrentHelp!.Title);
    }

    [Fact]
    public async Task Help_panel_toggle_is_remembered()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var shell = host.Get<ShellViewModel>();
        await host.Get<Navigator>().NavigateAsync<StudentsViewModel>();
        Assert.True(shell.IsHelpOpen); // shown by default for a new owner
        Assert.True(shell.ShowHelpPanel);

        shell.ToggleHelpCommand.Execute(null);
        Assert.False(shell.ShowHelpPanel);
        Assert.True(host.Get<IUserPreferences>().Get(PreferenceKeys.HelpCollapsed, false));
        var restarted = ActivatorUtilities.CreateInstance<ShellViewModel>(host.Services);
        Assert.False(restarted.IsHelpOpen);

        shell.ToggleHelpCommand.Execute(null);
        Assert.True(shell.IsHelpOpen);
        Assert.False(host.Get<IUserPreferences>().Get(PreferenceKeys.HelpCollapsed, true));
    }

    [Fact]
    public async Task Text_scale_is_saved_and_restored_at_startup()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var shell = host.Get<ShellViewModel>();
        Assert.Equal(1.0, shell.TextScale);
        Assert.Equal(["Normal", "Grand", "Très grand"], shell.TextScaleOptions.Select(o => o.Label));
        Assert.True(shell.TextScaleOptions[0].IsSelected);

        shell.TextScaleOptions[2].SelectCommand.Execute(null);
        Assert.Equal(1.3, shell.TextScale);
        Assert.True(shell.TextScaleOptions[2].IsSelected);
        Assert.False(shell.TextScaleOptions[0].IsSelected);
        Assert.Equal(1.3, host.Get<IUserPreferences>().Get(PreferenceKeys.TextScale, 1.0));

        var restarted = ActivatorUtilities.CreateInstance<ShellViewModel>(host.Services);
        Assert.Equal(1.3, restarted.TextScale);
        Assert.True(restarted.TextScaleOptions[2].IsSelected);

        // An odd stored value snaps to the nearest choice.
        host.Get<IUserPreferences>().Set(PreferenceKeys.TextScale, 1.12);
        Assert.Equal(1.15, ActivatorUtilities.CreateInstance<ShellViewModel>(host.Services).TextScale);
    }

    [Fact]
    public async Task Ctrl_N_opens_the_add_dialog_of_the_current_page()
    {
        await using var host = await UiHost.CreateAsync();
        var shell = host.Get<ShellViewModel>();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<StudentsViewModel>();
        Assert.Equal("Ajouter un élève", shell.PrimaryActionLabel);

        var add = shell.NewItemCommand.ExecuteAsync(null);
        Assert.IsType<StudentEditorDialogViewModel>(host.Get<DialogHost>().Current);
        await host.AnswerDialogAsync(confirm: false);
        await add;

        // Every page listed as having a primary action maps it to its Add command.
        foreach (var type in new[] { typeof(StudentsViewModel), typeof(ParentsViewModel), typeof(TeachersViewModel), typeof(SubjectsViewModel),
                     typeof(CoursesViewModel), typeof(GroupsViewModel), typeof(RoomsViewModel), typeof(ExpensesViewModel), typeof(ExamsViewModel),
                     typeof(SessionsViewModel), typeof(DocumentsViewModel) })
        {
            await nav.NavigateAsync(type);
            var page = Assert.IsAssignableFrom<IHasPrimaryAction>(nav.Current);
            Assert.NotNull(page.PrimaryCommand);
            Assert.Same(type.GetProperty("AddCommand")!.GetValue(nav.Current), page.PrimaryCommand);
            Assert.False(string.IsNullOrEmpty(page.PrimaryLabel));
        }

        // A page without one only shows a message.
        await nav.NavigateAsync<DashboardViewModel>();
        Assert.Null(shell.PrimaryActionLabel);
        await shell.NewItemCommand.ExecuteAsync(null);
        Assert.Null(host.Get<DialogHost>().Current);
        Assert.Contains("Ctrl+N", host.Get<Notifier>().Message);
    }

    [Fact]
    public async Task Ctrl_P_prints_the_reports()
    {
        await using var host = await UiHost.CreateAsync();
        var shell = host.Get<ShellViewModel>();
        await host.Get<Navigator>().NavigateAsync<ReportsViewModel>();
        var before = host.Get<FakePlatform>().Printed.Count;
        await shell.PrintPageCommand.ExecuteAsync(null);
        Assert.Equal(before + 1, host.Get<FakePlatform>().Printed.Count);

        await host.Get<Navigator>().NavigateAsync<StudentsViewModel>();
        await shell.PrintPageCommand.ExecuteAsync(null);
        Assert.Equal(before + 1, host.Get<FakePlatform>().Printed.Count);
    }

    [Fact]
    public async Task Refresh_back_and_ctrl_digit_navigation()
    {
        await using var host = await UiHost.CreateAsync();
        var shell = host.Get<ShellViewModel>();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<DashboardViewModel>();

        await shell.GoToCommand.ExecuteAsync("2");
        Assert.IsType<StudentsViewModel>(nav.Current);
        Assert.True(shell.NavGroups.SelectMany(g => g.Items).Single(i => i.Key == "students").IsActive);
        await shell.GoToCommand.ExecuteAsync("3");
        Assert.IsType<PaymentsViewModel>(nav.Current);
        await shell.GoToCommand.ExecuteAsync("4");
        Assert.IsType<AttendanceViewModel>(nav.Current);
        await shell.GoToCommand.ExecuteAsync("9");
        Assert.IsType<ReportsViewModel>(nav.Current);
        await shell.GoToCommand.ExecuteAsync("1");
        Assert.IsType<DashboardViewModel>(nav.Current);
        Assert.Equal(9, PageHelp.QuickPages.Distinct().Count());
        Assert.Equal("Ctrl+2", shell.NavGroups.SelectMany(g => g.Items).Single(i => i.Key == "students").Shortcut);

        // Alt+← / "← Retour"
        Assert.True(nav.CanGoBack);
        await shell.GoBackCommand.ExecuteAsync(null);
        Assert.IsType<ReportsViewModel>(nav.Current);

        // F5 reloads the page in place.
        var page = nav.Current!;
        await shell.RefreshPageCommand.ExecuteAsync(null);
        Assert.Same(page, nav.Current);
        Assert.False(page.HasError, page.Error);

        // Shortcuts do nothing while a dialog is open.
        await nav.NavigateAsync<StudentsViewModel>();
        var add = shell.NewItemCommand.ExecuteAsync(null);
        await shell.GoToCommand.ExecuteAsync("1");
        Assert.IsType<StudentsViewModel>(nav.Current);
        await host.AnswerDialogAsync(confirm: false);
        await add;
    }

    [Fact]
    public async Task F1_overlay_lists_the_shortcuts_and_closes_before_another_shortcut()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var shell = host.Get<ShellViewModel>();
        await host.Get<Navigator>().NavigateAsync<DashboardViewModel>();
        shell.ToggleShortcutsCommand.Execute(null);
        Assert.True(shell.IsShortcutsOpen);
        Assert.False(shell.IsAppInteractive);
        foreach (var keys in new[] { "F1", "Ctrl+K", "Ctrl+N", "Ctrl+P", "F5", "Alt+←", "Ctrl+1", "Ctrl+9", "Ctrl+L" })
            Assert.Contains(shell.Shortcuts, s => s.Keys == keys);

        await shell.GoToCommand.ExecuteAsync("2");
        Assert.False(shell.IsShortcutsOpen);
        Assert.True(shell.IsAppInteractive);
        Assert.IsType<StudentsViewModel>(host.Get<Navigator>().Current);

        shell.ToggleShortcutsCommand.Execute(null);
        shell.CloseShortcutsCommand.Execute(null);
        Assert.False(shell.IsShortcutsOpen);
    }
}
