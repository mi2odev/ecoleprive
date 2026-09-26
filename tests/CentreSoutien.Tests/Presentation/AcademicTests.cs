using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CentreSoutien.Presentation.ViewModels.Pages;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Tests.Presentation;

public class AcademicTests
{
    /// <summary>Waits for a dialog of the given type to be shown (dialogs open after an async initialization).</summary>
    private static async Task<T> DialogAsync<T>(TestHost host) where T : DialogViewModel
    {
        for (var i = 0; i < 200; i++)
        {
            if (host.Get<DialogHost>().Current is T d) return d;
            await Task.Delay(10);
        }
        throw new InvalidOperationException($"Dialog {typeof(T).Name} not shown (current: {host.Get<DialogHost>().Current?.GetType().Name}).");
    }

    private static async Task<CourseDetailViewModel> OpenCourseAsync(TestHost host, string name, int? groupId = null)
    {
        var course = (await host.Get<ICourseService>().ListAsync()).First(c => c.Name == name);
        await host.Get<Navigator>().NavigateAsync<CourseDetailViewModel>(new CourseDetailViewModel.Target(course.Id, groupId));
        var page = host.Page<CourseDetailViewModel>();
        Assert.False(page.HasError, page.Error);
        return page;
    }

    [Fact]
    public async Task Academic_lists_load_with_demo_data()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();

        await nav.NavigateAsync<SubjectsViewModel>();
        var subjects = host.Page<SubjectsViewModel>();
        Assert.False(subjects.HasError, subjects.Error);
        Assert.Equal(9, subjects.Rows.Count);
        var maths = subjects.Rows.Single(r => r.Name == "Mathématiques");
        Assert.Equal(2, maths.CourseCount);
        Assert.Equal(1, maths.TeacherCount);

        await nav.NavigateAsync<CoursesViewModel>();
        var courses = host.Page<CoursesViewModel>();
        Assert.False(courses.HasError, courses.Error);
        Assert.Equal(10, courses.Rows.Count);
        var math3 = courses.Rows.Single(r => r.Name == "Mathématiques · 3AS");
        Assert.Equal("A, B", math3.Groups);
        Assert.Equal("2 groupes", math3.Schedule);
        Assert.Contains("3AS", courses.Levels);
        courses.SelectedLevel = "4AM";
        Assert.All(courses.Rows, r => Assert.Equal("4AM", r.Level));
        courses.SelectedLevel = "Tous";
        courses.SearchText = "physique";
        Assert.Equal(2, courses.Rows.Count);

        await nav.NavigateAsync<GroupsViewModel>();
        var groups = host.Page<GroupsViewModel>();
        Assert.False(groups.HasError, groups.Error);
        Assert.Equal(11, groups.Rows.Count);
        Assert.Contains(groups.Rows, r => r.Name == "Mathématiques · 3AS B" && r.Teacher == "Karima Boudiaf" && r.Room == "Salle 1");

        await nav.NavigateAsync<RoomsViewModel>();
        var rooms = host.Page<RoomsViewModel>();
        Assert.False(rooms.HasError, rooms.Error);
        Assert.Equal(6, rooms.Rows.Count);
        // Saturday 14:32: Mathématiques 4AM A is in Salle 2 from 14h to 16h.
        var salle2 = rooms.Rows.Single(r => r.Name == "Salle 2");
        Assert.Equal(BadgeKind.Warn, salle2.Now.Kind);
        Assert.Contains("jusqu'à 16:00", salle2.Now.Text);
        Assert.Contains(rooms.Rows, r => r.Now.Kind == BadgeKind.Ok);
    }

    [Fact]
    public async Task Owner_creates_a_subject_a_room_and_a_course_through_dialogs()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();

        await nav.NavigateAsync<SubjectsViewModel>();
        var subjects = host.Page<SubjectsViewModel>();
        var add = subjects.AddCommand.ExecuteAsync(null);
        var subjectDialog = await DialogAsync<SubjectEditorDialogViewModel>(host);
        await subjectDialog.ConfirmCommand.ExecuteAsync(null);
        Assert.True(subjectDialog.HasError); // name is required
        subjectDialog.Name = "Économie";
        subjectDialog.ShortName = "Éco";
        await subjectDialog.ConfirmCommand.ExecuteAsync(null);
        await add;
        Assert.False(subjectDialog.HasError, subjectDialog.Error);
        Assert.Equal(10, subjects.Rows.Count);
        Assert.Contains(subjects.Rows, r => r.Name == "Économie" && r.ShortName == "Éco");

        // A subject used by courses cannot be deleted.
        var maths = subjects.Rows.Single(r => r.Name == "Mathématiques");
        var delete = ((IAsyncRelayCommand)maths.Delete).ExecuteAsync(null);
        await host.AnswerDialogAsync();
        await delete;
        Assert.True(subjects.HasError);
        Assert.Equal(10, subjects.Rows.Count);

        await nav.NavigateAsync<RoomsViewModel>();
        var rooms = host.Page<RoomsViewModel>();
        add = rooms.AddCommand.ExecuteAsync(null);
        var roomDialog = await DialogAsync<RoomEditorDialogViewModel>(host);
        roomDialog.Name = "Salle 5";
        roomDialog.Capacity = "20";
        roomDialog.Equipment = "Vidéoprojecteur";
        await roomDialog.ConfirmCommand.ExecuteAsync(null);
        await add;
        Assert.False(roomDialog.HasError, roomDialog.Error);
        Assert.Equal(7, rooms.Rows.Count);
        Assert.Contains(rooms.Rows, r => r.Name == "Salle 5" && r.Capacity == "20 places" && r.Equipment == "Vidéoprojecteur");

        await nav.NavigateAsync<CoursesViewModel>();
        add = host.Page<CoursesViewModel>().AddCommand.ExecuteAsync(null);
        var courseDialog = await DialogAsync<CourseEditorDialogViewModel>(host);
        courseDialog.SelectedSubject = courseDialog.SubjectOptions.Single(s => s.Label == "Économie");
        courseDialog.Level = "3as";
        courseDialog.Price = "3 500";
        await courseDialog.ConfirmCommand.ExecuteAsync(null);
        await add;
        Assert.False(courseDialog.HasError, courseDialog.Error);

        var detail = host.Page<CourseDetailViewModel>();
        Assert.False(detail.HasError, detail.Error);
        Assert.Equal("Économie · 3AS", detail.Name);
        Assert.False(detail.HasGroup);
        Assert.Equal("3 500", detail.Price);
        Assert.NotEmpty(detail.Eligible);
    }

    [Fact]
    public async Task Course_detail_changes_price_teacher_and_room_immediately()
    {
        await using var host = await UiHost.CreateAsync();
        var detail = await OpenCourseAsync(host, "Mathématiques · 3AS");
        Assert.Equal(["A", "B"], detail.GroupOptions.Select(g => g.Label));
        Assert.Equal("A", detail.SelectedGroup!.Label);
        Assert.Contains("Karima Boudiaf", detail.Subtitle);
        Assert.Equal("Sam. 9h–11h, Mar. 17h–19h", detail.Schedule);
        Assert.StartsWith("Recettes · Septembre 2026", detail.RevenueTitle);
        Assert.NotEqual("—", detail.AttendanceRate);

        // Price
        detail.Price = "5 000";
        await detail.Saving;
        Assert.False(detail.HasError, detail.Error);
        Assert.Equal(5000, (await host.Get<ICourseService>().ListAsync()).Single(c => c.Name == "Mathématiques · 3AS").MonthlyPrice);
        Assert.Equal("Prix mis à jour", host.Get<Notifier>().Message);

        // Teacher without conflict (Hakim Zitouni teaches Sunday/Thursday).
        detail.SelectedTeacher = detail.TeacherOptions.Single(t => t.Label == "Hakim Zitouni");
        await detail.Saving;
        Assert.False(detail.HasError, detail.Error);
        var groupA = await host.Get<IGroupService>().GetAsync(detail.GroupId!.Value);
        Assert.Equal("Zitouni", groupA!.Teacher!.LastName);
        Assert.Equal(2, groupA.Slots.Count);
        Assert.Contains("Hakim Zitouni", detail.Subtitle);

        // Teacher with a conflict (Omar Belhadj teaches Anglais 3AS on Saturday 9h–11h): refused and reverted.
        detail.SelectedTeacher = detail.TeacherOptions.Single(t => t.Label == "Omar Belhadj");
        await detail.Saving;
        Assert.True(detail.HasError);
        Assert.Contains("L'enseignant a déjà cours", detail.Error);
        Assert.Equal("Hakim Zitouni", detail.SelectedTeacher!.Label);

        // Room
        detail.SelectedRoom = detail.RoomOptions.Single(r => r.Label == "Salle 4");
        await detail.Saving;
        Assert.False(detail.HasError, detail.Error);
        Assert.Equal("Salle 4", (await host.Get<IGroupService>().GetAsync(detail.GroupId!.Value))!.Room!.Name);

        // Switching group keeps the page and shows the other group's settings.
        detail.SelectedGroup = detail.GroupOptions.Single(g => g.Label == "B");
        Assert.Equal("Salle 1", detail.SelectedRoom!.Label);
        Assert.Equal("Karima Boudiaf", detail.SelectedTeacher!.Label);
    }

    [Fact]
    public async Task Course_detail_adds_and_removes_a_student()
    {
        await using var host = await UiHost.CreateAsync();
        var detail = await OpenCourseAsync(host, "Physique · 2AS");
        Assert.NotEmpty(detail.Eligible);
        var before = detail.Enrolled.Count;
        if (detail.IsFull)
        {
            // Make room so the enrollment is accepted.
            var g = (await host.Get<IGroupService>().GetAsync(detail.GroupId!.Value))!;
            g.Capacity = before + 2;
            await host.Get<IGroupService>().SaveAsync(g, g.Slots);
            await detail.RefreshAsync();
        }

        var pick = detail.Eligible[0];
        detail.SelectedEligible = pick;
        await detail.AddStudentCommand.ExecuteAsync(null);
        Assert.False(detail.HasError, detail.Error);
        Assert.Equal(before + 1, detail.Enrolled.Count);
        Assert.DoesNotContain(detail.Eligible, e => e.Value == pick.Value);
        var row = detail.Enrolled.Single(r => r.Id == pick.Value);
        Assert.StartsWith($"{before + 1} / ", detail.Fill);

        var remove = ((IAsyncRelayCommand)row.Remove).ExecuteAsync(null);
        var confirm = await host.AnswerDialogAsync();
        Assert.IsType<ConfirmDialogViewModel>(confirm);
        await remove;
        Assert.False(detail.HasError, detail.Error);
        Assert.Equal(before, detail.Enrolled.Count);
        Assert.Contains(detail.Eligible, e => e.Value == pick.Value);

        // Opening a student from the list goes to the profile.
        if (detail.Enrolled.Count > 0)
        {
            await ((IAsyncRelayCommand)detail.Enrolled[0].Open).ExecuteAsync(null);
            Assert.Equal(detail.Enrolled[0].Name, host.Page<StudentDetailViewModel>().Name);
        }
    }

    [Fact]
    public async Task Group_editor_creates_a_group_with_two_slots_and_rejects_an_occupied_room()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<GroupsViewModel>();
        var groups = host.Page<GroupsViewModel>();

        var add = groups.AddCommand.ExecuteAsync(null);
        var dialog = await DialogAsync<GroupEditorDialogViewModel>(host);
        Assert.Equal(640, dialog.Width);
        dialog.SelectedCourse = dialog.CourseOptions.Single(c => c.Label == "Physique · 3AS");
        Assert.Equal("B", dialog.Name);
        dialog.SelectedTeacher = dialog.TeacherOptions.Single(t => t.Label == "Aucun");
        dialog.SelectedRoom = dialog.RoomOptions.Single(r => r.Label.StartsWith("Salle 4"));
        dialog.Capacity = "10";
        dialog.AddSlotCommand.Execute(null);
        dialog.AddSlotCommand.Execute(null);
        Assert.Equal(2, dialog.Slots.Count);
        dialog.Slots[0].Day = Options.Days.Single(d => d.Value == DayOfWeek.Wednesday);
        dialog.Slots[0].Start = "9h";
        dialog.Slots[0].End = "11:00";
        dialog.Slots[1].Day = Options.Days.Single(d => d.Value == DayOfWeek.Friday);
        dialog.Slots[1].Start = "10:00";
        dialog.Slots[1].End = "12:00";
        await dialog.CheckConflictsCommand.ExecuteAsync(null);
        Assert.False(dialog.HasConflicts, string.Join(" | ", dialog.Conflicts));
        await dialog.ConfirmCommand.ExecuteAsync(null);
        await add;
        Assert.False(dialog.HasError, dialog.Error);
        Assert.Equal(12, groups.Rows.Count);
        var created = groups.Rows.Single(r => r.Name == "Physique · 3AS B");
        Assert.Equal("Mer. 9h–11h, Ven. 10h–12h", created.Schedule);
        Assert.Equal("Salle 4", created.Room);

        // Second group in Salle 1 on Saturday 9h–11h: Mathématiques 3AS A is already there.
        add = groups.AddCommand.ExecuteAsync(null);
        var clash = await DialogAsync<GroupEditorDialogViewModel>(host);
        clash.SelectedCourse = clash.CourseOptions.Single(c => c.Label == "Physique · 3AS");
        clash.SelectedTeacher = clash.TeacherOptions.Single(t => t.Label == "Aucun");
        clash.SelectedRoom = clash.RoomOptions.Single(r => r.Label.StartsWith("Salle 1"));
        clash.AddSlotCommand.Execute(null);
        clash.Slots[0].Day = Options.Days.Single(d => d.Value == DayOfWeek.Saturday);
        clash.Slots[0].Start = "10:00";
        clash.Slots[0].End = "12:00";
        await clash.ConfirmCommand.ExecuteAsync(null);
        Assert.True(clash.HasError);
        Assert.True(clash.HasConflicts);
        Assert.Contains(clash.Conflicts, c => c.Contains("Salle déjà occupée") && c.Contains("Mathématiques · 3AS A"));
        Assert.Same(clash, host.Get<DialogHost>().Current);

        // Invalid time is reported too.
        clash.Slots[0].Start = "25h";
        await clash.ConfirmCommand.ExecuteAsync(null);
        Assert.Contains("heure invalide", clash.Error);
        Assert.Same(clash, host.Get<DialogHost>().Current);

        clash.Cancel();
        await add;
        Assert.Equal(12, groups.Rows.Count);

        // The new group has no students: it can be deleted.
        var delete = ((IAsyncRelayCommand)groups.Rows.Single(r => r.Name == "Physique · 3AS B").Delete).ExecuteAsync(null);
        await host.AnswerDialogAsync();
        await delete;
        Assert.False(groups.HasError, groups.Error);
        Assert.Equal(11, groups.Rows.Count);
    }

    [Fact]
    public async Task Course_detail_navigates_to_attendance_and_back_to_courses()
    {
        await using var host = await UiHost.CreateAsync();
        var detail = await OpenCourseAsync(host, "Anglais · 3AS");
        var groupId = detail.GroupId;
        await detail.OpenAttendanceCommand.ExecuteAsync(null);
        var attendance = host.Page<AttendanceViewModel>();
        Assert.Equal(new AttendanceViewModel.Target(host.Clock.Now.Date, groupId), attendance.LastParameter);

        detail = await OpenCourseAsync(host, "Anglais · 3AS");
        await detail.BackCommand.ExecuteAsync(null);
        Assert.IsType<CoursesViewModel>(host.Get<Navigator>().Current);

        // A course with enrollments cannot be deleted.
        detail = await OpenCourseAsync(host, "Anglais · 3AS");
        var delete = detail.DeleteCourseCommand.ExecuteAsync(null);
        await host.AnswerDialogAsync();
        await delete;
        Assert.True(detail.HasError);
        Assert.Same(detail, host.Get<Navigator>().Current);
    }
}
