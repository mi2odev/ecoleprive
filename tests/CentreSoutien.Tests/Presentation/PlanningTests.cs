using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CentreSoutien.Presentation.ViewModels.Pages;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Tests.Presentation;

public class PlanningTests
{
    [Fact]
    public async Task Schedule_shows_the_22_weekly_slots_in_non_overlapping_lanes()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<ScheduleViewModel>();
        var page = host.Page<ScheduleViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.Equal(22, page.BlockCount);
        Assert.Equal("Semaine du 26 septembre au 2 octobre 2026", page.WeekLabel);

        var visible = page.Days.Where(d => d.IsVisible).ToList();
        Assert.Equal(6, visible.Count); // Saturday → Thursday, no Friday slot in the demo timetable.
        Assert.Equal("Samedi 26", visible[0].Label);
        Assert.True(visible[0].IsToday);
        Assert.True(visible[0].ShowNow);
        Assert.Single(page.Days, d => d.IsToday);

        foreach (var day in page.Days)
        foreach (var lane in day.Lanes)
        {
            var blocks = lane.Blocks.OrderBy(b => b.Start).ToList();
            for (var i = 1; i < blocks.Count; i++)
                Assert.True(blocks[i - 1].End <= blocks[i].Start, $"{day.Label}: {blocks[i - 1].Name} overlaps {blocks[i].Name}");
        }
        Assert.True(visible[0].LaneCount >= 3);
        // 14:32 on Saturday: the 14h–16h group is live.
        Assert.Contains(visible[0].Blocks, b => b.IsLive && b.Time == "14h–16h");
        var first = visible[0].Blocks.OrderBy(b => b.Start).First();
        Assert.Equal((9 - 8) * 52 + 2, first.Top);
        Assert.Equal(2 * 52 - 4, first.Height);

        page.NextWeekCommand.Execute(null);
        Assert.Equal("Semaine du 3 au 9 octobre 2026", page.WeekLabel);
        Assert.DoesNotContain(page.Days, d => d.IsToday);
        Assert.Equal(22, page.BlockCount);

        page.SelectedTeacher = page.Teachers.First(t => t.Label.Contains("Boudiaf"));
        Assert.Equal(6, page.BlockCount);
    }

    [Fact]
    public async Task Sessions_page_generates_next_week_from_the_timetable()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<SessionsViewModel>();
        var page = host.Page<SessionsViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.Equal(22, page.SessionCount);
        Assert.Equal("Samedi 26 septembre", page.Days[0].Title);

        await page.NextWeekCommand.ExecuteAsync(null);
        Assert.Equal(0, page.SessionCount);
        Assert.True(page.IsEmpty);
        await page.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(22, page.SessionCount);
        Assert.Equal("22 séances créées", host.Get<Notifier>().Message);
        Assert.All(page.Days.SelectMany(d => d.Rows), r => Assert.Equal("Prévue", r.Status.Text));

        // Generating again creates nothing.
        await page.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(22, page.SessionCount);

        // Cancel then restore a session.
        var row = page.Days[0].Rows[0];
        var cancel = ((IAsyncRelayCommand)row.ToggleCancel).ExecuteAsync(null);
        await host.AnswerDialogAsync();
        await cancel;
        Assert.Equal("Annulée", page.Days[0].Rows.First(r => r.Id == row.Id).Status.Text);
        page.SelectedStatus = page.StatusFilters.First(f => f.Value == SessionStatus.Cancelled);
        Assert.Equal(1, page.SessionCount);
    }

    [Fact]
    public async Task Session_editor_creates_an_ad_hoc_session_with_group_defaults()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<SessionsViewModel>();
        var page = host.Page<SessionsViewModel>();
        var add = page.AddCommand.ExecuteAsync(null);
        var dialog = (SessionEditorDialogViewModel)host.Get<DialogHost>().Current!;
        dialog.SelectedGroup = dialog.GroupOptions.First(g => g.Label.StartsWith("Philosophie"));
        Assert.Equal("Salle 4", dialog.SelectedRoom?.Label);
        Assert.Contains("Zitouni", dialog.SelectedTeacher?.Label);
        dialog.Start = "18h";
        dialog.End = "19:30";
        await dialog.ConfirmCommand.ExecuteAsync(null);
        await add;
        Assert.False(dialog.HasError, dialog.Error);
        Assert.Equal(23, page.SessionCount);
        Assert.Contains(page.Days[0].Rows, r => r.Time == "18:00–19:30" && r.Group.StartsWith("Philosophie"));
    }

    [Fact]
    public async Task Attendance_all_present_then_save_persists()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<AttendanceViewModel>();
        var page = host.Page<AttendanceViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.Equal("Samedi 26 septembre 2026", page.DayLabel);
        Assert.Equal(8, page.SessionItems.Count);
        Assert.Contains(page.SessionItems, i => i.Progress == "À faire");

        var todo = page.SessionItems.First(i => i.Progress == "À faire");
        await ((IAsyncRelayCommand)todo.Select).ExecuteAsync(null);
        Assert.True(todo.IsSelected);
        Assert.Same(todo, page.Selected);
        Assert.NotEmpty(page.Rows);
        Assert.All(page.Rows, r => Assert.Null(r.Status));
        Assert.Equal(page.Rows.Count, page.Counts.Single(c => c.Label == "Non saisis").Value);

        page.AllPresentCommand.Execute(null);
        Assert.Equal(page.Rows.Count, page.Counts.Single(c => c.Label == "Présents").Value);
        Assert.All(page.Rows, r => Assert.True(r.Choices.Single(c => c.IsOn).Status == AttendanceStatus.Present));
        page.Rows[0].Choices.Single(c => c.Status == AttendanceStatus.Absent).Select.Execute(null);
        Assert.Equal(1, page.Counts.Single(c => c.Label == "Absents").Value);

        await page.SaveCommand.ExecuteAsync(null);
        Assert.False(page.HasError, page.Error);
        Assert.StartsWith("Présences enregistrées · ", host.Get<Notifier>().Message);
        Assert.Equal($"{page.Rows.Count} / {page.Rows.Count} saisis", todo.Progress);

        // Reload through the dashboard-style target: marks are persisted.
        await nav.NavigateAsync<AttendanceViewModel>(new AttendanceViewModel.Target(new DateTime(2026, 9, 26), todo.GroupId));
        var reloaded = host.Page<AttendanceViewModel>();
        Assert.Equal(todo.Id, reloaded.Selected?.Id);
        Assert.Equal(AttendanceStatus.Absent, reloaded.Rows[0].Status);
        Assert.All(reloaded.Rows.Skip(1), r => Assert.Equal(AttendanceStatus.Present, r.Status));
        Assert.Equal(0, reloaded.Counts.Single(c => c.Label == "Non saisis").Value);
    }

    [Fact]
    public async Task Attendance_on_a_day_without_timetable_shows_empty_state()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<AttendanceViewModel>(new AttendanceViewModel.Target(new DateTime(2026, 10, 2)));
        var page = host.Page<AttendanceViewModel>();
        Assert.True(page.HasNoSession);
        Assert.False(page.HasSheet);

        page.Day = new DateTime(2026, 10, 3); // next Saturday: sessions are generated on the fly
        await Task.Delay(50);
        for (var i = 0; i < 150 && page.SessionItems.Count == 0; i++) await Task.Delay(20);
        Assert.Equal(8, page.SessionItems.Count);
        Assert.All(page.SessionItems, s => Assert.Equal("À faire", s.Progress));
    }

    [Fact]
    public async Task Exams_list_shows_the_22_demo_evaluations()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<ExamsViewModel>();
        var page = host.Page<ExamsViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.Equal(22, page.Rows.Count);
        Assert.All(page.Rows, r => Assert.NotEqual("—", r.Average));
        Assert.All(page.Rows, r => Assert.Matches(@"^\d+ / \d+$", r.Entered));

        page.SelectedType = page.TypeFilters.First(t => t.Value == ExamType.Test);
        Assert.Equal(11, page.Rows.Count);
        page.SelectedGroup = page.GroupOptions[1];
        Assert.Single(page.Rows);

        await ((IAsyncRelayCommand)page.Rows[0].EnterGrades).ExecuteAsync(null);
        var grades = host.Page<GradesViewModel>();
        Assert.Equal(page.GroupOptions[1].Value, grades.SelectedGroup?.Value);
        Assert.Equal(page.Rows[0].Id, grades.SelectedExam?.Value);
        Assert.NotEmpty(grades.Rows);
    }

    [Fact]
    public async Task Owner_creates_an_exam_then_enters_grades()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<ExamsViewModel>();
        var exams = host.Page<ExamsViewModel>();
        var add = exams.AddCommand.ExecuteAsync(null);
        var dialog = (ExamEditorDialogViewModel)host.Get<DialogHost>().Current!;
        dialog.SelectedGroup = dialog.GroupOptions[0];
        dialog.ExamTitle = "Examen trimestriel";
        dialog.SelectedType = dialog.Types.First(t => t.Value == ExamType.Exam);
        dialog.MaxScore = "40";
        dialog.Coefficient = "2";
        await dialog.ConfirmCommand.ExecuteAsync(null);
        await add;
        Assert.False(dialog.HasError, dialog.Error);
        Assert.Equal(23, exams.Rows.Count);
        var row = exams.Rows.Single(r => r.Title == "Examen trimestriel");
        Assert.Equal("Examen", row.Type);
        Assert.Equal("—", row.Average);

        await ((IAsyncRelayCommand)row.EnterGrades).ExecuteAsync(null);
        var page = host.Page<GradesViewModel>();
        Assert.Equal("Examen trimestriel", page.ExamTitle);
        Assert.All(page.Rows, r => Assert.Equal("", r.Score));
        page.Rows[0].Score = "30,5";
        page.Rows[1].Score = "14,5";
        page.Rows[1].Comment = "Peut mieux faire";
        await page.SaveCommand.ExecuteAsync(null);
        Assert.False(page.HasError, page.Error);

        await nav.NavigateAsync<GradesViewModel>(new GradesViewModel.Target(row.GroupId, row.Id));
        var reloaded = host.Page<GradesViewModel>();
        Assert.Equal("30,5", reloaded.Rows[0].Score);
        Assert.Equal("14,5", reloaded.Rows[1].Score);
        Assert.Equal("Peut mieux faire", reloaded.Rows[1].Comment);
        // (30.5/40 + 14.5/40) / 2 × 20 = 11.25
        Assert.Equal((11.25m).ToString("0.00"), reloaded.Summary[0].Value);
        Assert.Equal("1 / 2", reloaded.Summary[3].Value);
        Assert.NotEmpty(reloaded.GroupAverages);

        var sheet = await host.Get<IExamService>().GetGradeSheetAsync(row.Id);
        Assert.Equal(30.5m, sheet.Lines[0].Score);
        Assert.Equal(14.5m, sheet.Lines[1].Score);
    }

    [Fact]
    public async Task Grade_above_the_maximum_is_rejected_with_an_error()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();
        var groups = await host.Get<IGroupService>().ListAsync();
        await nav.NavigateAsync<GradesViewModel>(groups[0].Id);
        var page = host.Page<GradesViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.Equal(groups[0].Id, page.SelectedGroup?.Value);
        Assert.Equal(2, page.ExamOptions.Count);
        Assert.True(page.HasExam);

        var before = page.Rows[0].Score;
        page.Rows[0].Score = "25";
        await page.SaveCommand.ExecuteAsync(null);
        Assert.True(page.HasError);
        Assert.True(page.Rows[0].IsInvalid);
        Assert.Contains("entre 0 et 20", page.Error);

        var sheet = await host.Get<IExamService>().GetGradeSheetAsync(page.SelectedExam!.Value);
        Assert.Equal(before, sheet.Lines[0].Score?.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? "");
    }
}
