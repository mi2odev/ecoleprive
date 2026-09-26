using System.Reflection;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Pages;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Tests.Presentation;

/// <summary>
/// Usability pass ("less clutter"): actions regrouped in "Plus" menus must all stay reachable and working,
/// list pages expose "no data yet" / "no result" flags for their empty states, and attendance can be taken with the keyboard.
/// </summary>
public class DeclutterTests
{
    // Commands exposed by the profile pages before the actions were regrouped: none may disappear.
    public static TheoryData<string, string[]> DetailCommands => new()
    {
        { nameof(StudentDetailViewModel), ["ShowTab", "AddPayment", "Edit", "Enroll", "ChangeGroup", "ApplyDiscount", "AddDocument", "PrintDocuments", "ToggleActive", "Delete", "OpenParent", "EnterGrades", "Back"] },
        { nameof(TeacherDetailViewModel), ["RecordPayment", "Edit", "OpenSchedule", "AddDocument", "ToggleActive", "Delete", "Back"] },
        { nameof(GroupDetailViewModel), ["SavePrice", "AddStudent", "NewGroup", "EditGroup", "DeleteGroup", "OpenAttendance", "Back"] },
    };

    [Theory]
    [MemberData(nameof(DetailCommands))]
    public void Every_profile_command_still_exists_and_is_bound_in_the_view(string viewModel, string[] commands)
    {
        var type = typeof(StudentDetailViewModel).Assembly.GetType($"{typeof(StudentDetailViewModel).Namespace}.{viewModel}")!;
        var exposed = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(IRelayCommand).IsAssignableFrom(p.PropertyType)).Select(p => p.Name).ToHashSet();
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CentreSoutien.Desktop", "Views", "Pages", viewModel[..^"Model".Length] + ".xaml"));
        foreach (var c in commands)
        {
            Assert.Contains(c + "Command", exposed);
            Assert.Contains($"{{Binding {c}Command}}", xaml);
        }
    }

    [Theory]
    [MemberData(nameof(DetailCommands))]
    public async Task Every_profile_command_still_executes(string viewModel, string[] commands)
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();
        var now = host.Clock.GetLocalNow().DateTime;
        var (pageType, parameter) = viewModel switch
        {
            nameof(StudentDetailViewModel) => (typeof(StudentDetailViewModel), (object)(await host.Get<IStudentService>().ListAsync(now)).First(s => s.IsActive).Id),
            nameof(TeacherDetailViewModel) => (typeof(TeacherDetailViewModel), (await host.Get<ITeacherService>().ListAsync(now)).First(t => t.IsActive).Id),
            _ => (typeof(GroupDetailViewModel), new GroupDetailViewModel.Target((await host.Get<IGroupService>().ListAsync()).First().Id)),
        };

        foreach (var name in commands)
        {
            await nav.NavigateAsync(pageType, parameter);
            var page = nav.Current!;
            Assert.IsType(pageType, page);
            Assert.False(page.HasError, page.Error);
            var command = (IRelayCommand)pageType.GetProperty(name + "Command")!.GetValue(page)!;
            var arg = name == "ShowTab" ? "payments" : null;
            Assert.True(command.CanExecute(arg), name);
            if (command is IAsyncRelayCommand async)
            {
                var run = async.ExecuteAsync(arg);
                // Dialogs opened by the command (editors, confirmations) are cancelled: nothing is changed.
                for (var i = 0; i < 200 && !run.IsCompleted; i++)
                {
                    if (await host.AnswerDialogAsync(confirm: false) is null) await Task.Delay(10);
                }
                await run;
            }
            else
            {
                command.Execute(arg);
            }
            Assert.Null(host.Get<DialogHost>().Current);
        }
    }

    // Pages whose "getting started" empty state is driven by a flag.
    public static TheoryData<Type, string> EmptyStates => new()
    {
        { typeof(StudentsViewModel), "HasNoData" },
        { typeof(ParentsViewModel), "HasNoData" },
        { typeof(TeachersViewModel), "HasNoData" },
        { typeof(SubjectsViewModel), "HasNoData" },
        { typeof(GroupsViewModel), "HasNoData" },
        { typeof(RoomsViewModel), "HasNoData" },
        { typeof(SessionsViewModel), "HasNoData" },
        { typeof(ExamsViewModel), "HasNoData" },
        { typeof(ExpensesViewModel), "HasNoData" },
        { typeof(PaymentsViewModel), "HasNoData" },
        { typeof(PaymentsViewModel), "HasNoReceipts" },
        { typeof(PaymentsViewModel), "HasNoReminders" },
        { typeof(TeacherPaymentsViewModel), "HasNoData" },
        { typeof(PaymentsViewModel), "HasNoDiscounts" },
        { typeof(GradesViewModel), "HasNoGroup" },
        { typeof(AttendanceViewModel), "HasNoSession" },
    };

    [Theory]
    [MemberData(nameof(EmptyStates))]
    public async Task Empty_state_flag_is_set_on_a_fresh_database_and_cleared_with_demo_data(Type pageType, string flag)
    {
        await using (var fresh = await UiHost.CreateAsync(demo: false))
        {
            var page = await OpenAsync(fresh, pageType);
            Assert.True(Flag(page, flag), $"{pageType.Name}.{flag} should be true on an empty database");
            if (pageType.GetProperty("HasNoResults") is not null) Assert.False(Flag(page, "HasNoResults"));
        }
        await using var demo = await UiHost.CreateAsync();
        var loaded = await OpenAsync(demo, pageType);
        Assert.False(Flag(loaded, flag), $"{pageType.Name}.{flag} should be false with the demo data");
    }

    [Fact]
    public async Task Documents_empty_state_follows_the_documents()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var page = (DocumentsViewModel)await OpenAsync(host, typeof(DocumentsViewModel));
        Assert.True(page.HasNoData);
        Assert.False(page.HasNoResults);
        page.SearchText = "contrat";
        Assert.False(page.HasNoResults); // nothing to filter: still the "no data" state
    }

    [Fact]
    public async Task Students_search_without_match_shows_no_result_not_no_data_and_filters_can_be_reset()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<StudentsViewModel>();
        var page = host.Page<StudentsViewModel>();
        var all = page.Rows.Count;
        Assert.False(page.HasNoData);
        Assert.False(page.HasNoResults);

        page.SearchText = "zzz-aucun-eleve";
        Assert.Empty(page.Rows);
        Assert.True(page.HasNoResults);
        Assert.False(page.HasNoData);

        page.SelectedState = "Payé";
        page.ClearFiltersCommand.Execute(null);
        Assert.Equal("", page.SearchText);
        Assert.Equal("Tous", page.SelectedLevel);
        Assert.Equal("Tous", page.SelectedState);
        Assert.Equal(all, page.Rows.Count);
        Assert.False(page.HasNoResults);

        // On an empty database a search is not a "no result": the page still invites to add students.
        await using var fresh = await UiHost.CreateAsync(demo: false);
        await fresh.Get<Navigator>().NavigateAsync<StudentsViewModel>();
        var empty = fresh.Page<StudentsViewModel>();
        empty.SearchText = "abc";
        Assert.True(empty.HasNoData);
        Assert.False(empty.HasNoResults);
    }

    [Fact]
    public async Task Filtered_lists_distinguish_no_result_from_no_data()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();

        await nav.NavigateAsync<TeachersViewModel>();
        var teachers = host.Page<TeachersViewModel>();
        teachers.SearchText = "zzz";
        Assert.True(teachers.HasNoResults);
        teachers.ClearFiltersCommand.Execute(null);
        Assert.False(teachers.HasNoResults);
        Assert.NotEmpty(teachers.Rows);

        await nav.NavigateAsync<GroupsViewModel>();
        var groups = host.Page<GroupsViewModel>();
        groups.SearchText = "zzz";
        Assert.True(groups.HasNoResults);
        Assert.False(groups.HasNoData);
        groups.ClearFiltersCommand.Execute(null);
        Assert.NotEmpty(groups.Rows);

        await nav.NavigateAsync<PaymentsViewModel>();
        var payments = host.Page<PaymentsViewModel>();
        payments.SearchText = "zzz";
        Assert.True(payments.HasNoResults);
        payments.ClearFiltersCommand.Execute(null);
        Assert.False(payments.HasNoResults);
        Assert.NotEmpty(payments.Rows);

        await nav.NavigateAsync<SessionsViewModel>();
        var sessions = host.Page<SessionsViewModel>();
        if (sessions.HasNoData) await sessions.GenerateCommand.ExecuteAsync(null);
        Assert.False(sessions.HasNoData);
        sessions.SelectedStatus = sessions.StatusFilters.Single(f => f.Value == SessionStatus.Cancelled);
        if (sessions.SessionCount == 0)
        {
            Assert.True(sessions.HasNoResults);
            sessions.ClearFiltersCommand.Execute(null);
            Assert.False(sessions.HasNoResults);
            Assert.True(sessions.SessionCount > 0);
        }
    }

    [Fact]
    public async Task Empty_state_shortcuts_open_the_right_pages()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var nav = host.Get<Navigator>();

        await nav.NavigateAsync<PaymentsViewModel>();
        await host.Page<PaymentsViewModel>().OpenStudentsCommand.ExecuteAsync(null);
        Assert.IsType<StudentsViewModel>(nav.Current);

        await nav.NavigateAsync<TeacherPaymentsViewModel>();
        await host.Page<TeacherPaymentsViewModel>().OpenTeachersCommand.ExecuteAsync(null);
        Assert.IsType<TeachersViewModel>(nav.Current);

        await nav.NavigateAsync<GradesViewModel>();
        await host.Page<GradesViewModel>().OpenGroupsCommand.ExecuteAsync(null);
        Assert.IsType<GroupsViewModel>(nav.Current);

        await nav.NavigateAsync<AttendanceViewModel>();
        var attendance = host.Page<AttendanceViewModel>();
        await attendance.GenerateDayCommand.ExecuteAsync(null);
        Assert.False(attendance.HasError, attendance.Error);
        Assert.True(attendance.HasNoSession);
        await attendance.OpenScheduleCommand.ExecuteAsync(null);
        Assert.IsType<ScheduleViewModel>(nav.Current);
        await nav.NavigateAsync<AttendanceViewModel>();
        await host.Page<AttendanceViewModel>().OpenSessionsCommand.ExecuteAsync(null);
        Assert.IsType<SessionsViewModel>(nav.Current);
    }

    [Fact]
    public async Task Attendance_letters_mark_the_students()
    {
        Assert.Equal(AttendanceStatus.Present, AttendanceStudentRow.StatusForKey("P"));
        Assert.Equal(AttendanceStatus.Absent, AttendanceStudentRow.StatusForKey("a"));
        Assert.Equal(AttendanceStatus.Late, AttendanceStudentRow.StatusForKey("R"));
        Assert.Equal(AttendanceStatus.Excused, AttendanceStudentRow.StatusForKey("e"));
        Assert.Null(AttendanceStudentRow.StatusForKey("X"));
        Assert.Null(AttendanceStudentRow.StatusForKey(null));

        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<AttendanceViewModel>();
        var page = host.Page<AttendanceViewModel>();
        var todo = page.SessionItems.First(i => i.Progress == "À faire");
        await ((IAsyncRelayCommand)todo.Select).ExecuteAsync(null);
        Assert.True(page.Rows.Count >= 3);

        var (first, second, third) = (page.Rows[0], page.Rows[1], page.Rows[2]);
        Assert.False(first.MarkKeyCommand.CanExecute("X"));
        first.MarkKeyCommand.Execute("X");
        Assert.Null(first.Status);

        first.MarkKeyCommand.Execute("A");
        second.MarkKeyCommand.Execute("r");
        third.MarkKeyCommand.Execute("E");
        Assert.Equal(AttendanceStatus.Absent, first.Status);
        Assert.Equal(AttendanceStatus.Late, second.Status);
        Assert.Equal(AttendanceStatus.Excused, third.Status);
        Assert.True(first.Choices.Single(c => c.Status == AttendanceStatus.Absent).IsOn);
        Assert.Equal(1, page.Counts.Single(c => c.Label == "Absents").Value);
        Assert.Equal(1, page.Counts.Single(c => c.Label == "Retards").Value);
        Assert.Equal(1, page.Counts.Single(c => c.Label == "Excusés").Value);

        first.MarkKeyCommand.Execute("P");
        Assert.Equal(AttendanceStatus.Present, first.Status);

        // "Tous présents" is kept alongside the shortcuts.
        page.AllPresentCommand.Execute(null);
        Assert.All(page.Rows, r => Assert.Equal(AttendanceStatus.Present, r.Status));
    }

    private static async Task<PageViewModel> OpenAsync(TestHost host, Type pageType)
    {
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync(pageType);
        var page = nav.Current!;
        Assert.IsType(pageType, page);
        // Some pages finish loading from property-change handlers: give them a moment.
        for (var i = 0; i < 50 && page.IsBusy; i++) await Task.Delay(20);
        Assert.False(page.HasError, page.Error);
        return page;
    }

    private static bool Flag(object page, string name) => (bool)page.GetType().GetProperty(name)!.GetValue(page)!;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CentreSoutien.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}
