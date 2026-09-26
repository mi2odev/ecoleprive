using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CentreSoutien.Presentation.ViewModels.Pages;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Tests.Presentation;

public class PeopleTests
{
    [Fact]
    public async Task Parents_list_loads_and_selecting_a_parent_shows_the_children()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<ParentsViewModel>();
        var page = host.Page<ParentsViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.NotEmpty(page.Rows);
        Assert.True(page.HasSelection);
        Assert.Equal(page.Rows[0].Id, page.SelectedId);

        var withTwo = page.Rows.FirstOrDefault(r => r.ChildCount > 1) ?? page.Rows.Last();
        await ((IAsyncRelayCommand)withTwo.Select).ExecuteAsync(null);
        Assert.Equal(withTwo.Id, page.SelectedId);
        Assert.True(withTwo.IsSelected);
        Assert.Single(page.Rows, r => r.IsSelected);
        Assert.Equal(withTwo.Name, page.SelectedName);
        Assert.Equal(withTwo.ChildCount, page.Children.Count);
        Assert.Contains(page.Info, f => f.Label == "Téléphone");
        Assert.Equal(withTwo.BalanceValue > 0, page.BalanceLabel.StartsWith("Reste"));

        // Search by a child's name.
        var child = page.Children[0];
        page.SearchText = child.Name;
        Assert.Contains(page.Rows, r => r.Id == withTwo.Id);

        // Opening a child goes to the student profile.
        await ((IAsyncRelayCommand)child.Open).ExecuteAsync(null);
        Assert.Equal(child.Name, host.Page<StudentDetailViewModel>().Name);

        // The student profile links back to the parent, selected.
        await host.Page<StudentDetailViewModel>().OpenParentCommand.ExecuteAsync(null);
        Assert.Equal(withTwo.Id, host.Page<ParentsViewModel>().SelectedId);
    }

    [Fact]
    public async Task Owner_adds_edits_and_deletes_a_parent()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<ParentsViewModel>();
        var page = host.Page<ParentsViewModel>();
        var count = page.Rows.Count;

        var add = page.AddCommand.ExecuteAsync(null);
        var editor = (ParentEditorDialogViewModel)host.Get<DialogHost>().Current!;
        Assert.Equal("Nouveau parent", editor.Title);
        await editor.ConfirmCommand.ExecuteAsync(null);
        Assert.True(editor.HasError); // name is required
        editor.FullName = "Mme Zeroual";
        editor.Relation = "Mère";
        editor.Phone = "0661 11 22 33";
        editor.Profession = "Médecin";
        await editor.ConfirmCommand.ExecuteAsync(null);
        await add;
        Assert.False(editor.HasError, editor.Error);
        Assert.Equal(count + 1, page.Rows.Count);
        Assert.Equal(editor.SavedId, page.SelectedId);
        Assert.Equal("Mme Zeroual", page.SelectedName);
        Assert.Empty(page.Children);

        var edit = page.EditCommand.ExecuteAsync(null);
        var editor2 = (ParentEditorDialogViewModel)host.Get<DialogHost>().Current!;
        Assert.Equal("Médecin", editor2.Profession);
        editor2.Phone2 = "0555 00 00 01";
        await editor2.ConfirmCommand.ExecuteAsync(null);
        await edit;
        Assert.Contains(page.Info, f => f.Label == "Téléphone 2" && f.Value == "0555 00 00 01");

        // Add a child from the parent card: the student editor is pre-set with this parent.
        var addChild = page.AddChildCommand.ExecuteAsync(null);
        var student = (StudentEditorDialogViewModel)host.Get<DialogHost>().Current!;
        Assert.Equal(page.SelectedId, student.SelectedParent?.Value);
        student.FirstName = "Lina";
        student.LastName = "Zeroual";
        student.Level = "2AS";
        await student.ConfirmCommand.ExecuteAsync(null);
        await addChild;
        Assert.False(student.HasError, student.Error);
        Assert.Single(page.Children);
        var childId = page.Children[0].Id;

        var delete = page.DeleteCommand.ExecuteAsync(null);
        await host.AnswerDialogAsync();
        await delete;
        Assert.False(page.HasError, page.Error);
        Assert.Equal(count, page.Rows.Count);
        Assert.DoesNotContain(page.Rows, r => r.Name == "Mme Zeroual");

        // The child is kept, without parent.
        var kept = await host.Get<IStudentService>().GetAsync(childId);
        Assert.NotNull(kept);
        Assert.Null(kept!.ParentId);
    }

    [Fact]
    public async Task Teachers_list_shows_the_nine_demo_teachers_and_filters()
    {
        await using var host = await UiHost.CreateAsync();
        await host.Get<Navigator>().NavigateAsync<TeachersViewModel>();
        var page = host.Page<TeachersViewModel>();
        Assert.False(page.HasError, page.Error);
        Assert.Equal(9, page.Rows.Count);
        Assert.All(page.Rows, r => Assert.False(string.IsNullOrEmpty(r.Rule)));
        Assert.Contains(page.Rows, r => r.Groups > 0 && r.Students > 0);

        page.SelectedFilter = "Inactifs";
        Assert.All(page.Rows, r => Assert.Equal("Inactif", r.Status.Text));
        page.SelectedFilter = "Tous";
        var first = page.Rows[0];
        page.SearchText = first.Name;
        Assert.Contains(page.Rows, r => r.Id == first.Id);
        page.SearchText = "zzzz";
        Assert.Empty(page.Rows);
    }

    [Fact]
    public async Task Teacher_profile_shows_groups_earnings_and_history_and_records_a_payment()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<TeachersViewModel>();
        var row = host.Page<TeachersViewModel>().Rows.First(r => r.Groups > 0);
        await ((IAsyncRelayCommand)row.Open).ExecuteAsync(null);

        var profile = host.Page<TeacherDetailViewModel>();
        Assert.False(profile.HasError, profile.Error);
        Assert.Equal(row.Name, profile.Name);
        Assert.Contains("Au centre depuis", profile.Subtitle);
        Assert.Equal(row.Groups, profile.Groups.Count);
        Assert.All(profile.Groups, g => Assert.Contains(" / ", g.Fill));
        Assert.Equal($"Élèves ({row.Students})", profile.StudentsTitle);
        Assert.StartsWith("Gains · Septembre 2026", profile.EarningsTitle);
        Assert.False(string.IsNullOrEmpty(profile.Rule));
        Assert.NotEmpty(profile.History); // demo data pays previous months
        var before = profile.History.Count;

        var pay = profile.RecordPaymentCommand.ExecuteAsync(null);
        var dialog = (TeacherPaymentDialogViewModel)host.Get<DialogHost>().Current!;
        dialog.Amount = "5000";
        dialog.SelectedMethod = dialog.Methods.First(m => m.Value == PaymentMethod.Cash);
        await dialog.ConfirmCommand.ExecuteAsync(null);
        await pay;
        Assert.False(dialog.HasError, dialog.Error);
        Assert.Equal(before + 1, profile.History.Count);
        Assert.Contains(profile.History, h => h.Month == "Septembre 2026" && h.Method == "Espèces");
        Assert.Contains("Versé", profile.PaidSummary);

        // Delete that payment again.
        var added = profile.History.First(h => h.Month == "Septembre 2026" && h.Method == "Espèces");
        var del = ((IAsyncRelayCommand)added.Delete).ExecuteAsync(null);
        await host.AnswerDialogAsync();
        await del;
        Assert.Equal(before, profile.History.Count);

        // Group row opens the group page.
        await ((IAsyncRelayCommand)profile.Groups[0].Open).ExecuteAsync(null);
        Assert.IsType<GroupDetailViewModel>(nav.Current);
    }

    [Fact]
    public async Task Owner_creates_a_teacher_through_the_editor_and_lands_on_the_profile()
    {
        await using var host = await UiHost.CreateAsync();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<TeachersViewModel>();
        var add = host.Page<TeachersViewModel>().AddCommand.ExecuteAsync(null);
        var editor = (TeacherEditorDialogViewModel)host.Get<DialogHost>().Current!;
        Assert.Equal("Nouvel enseignant", editor.Title);
        Assert.Equal("2026", editor.StartYear);
        Assert.Equal(CompensationType.Percentage, editor.SelectedCompensation!.Value);
        Assert.Equal("40", editor.CompensationValue);
        Assert.Equal("Aucune", editor.SubjectOptions[0].Label);

        // Switching the rule proposes the default value from the settings.
        editor.SelectedCompensation = editor.CompensationTypes.First(c => c.Value == CompensationType.PerSession);
        Assert.Equal("2 000", editor.CompensationValue);
        Assert.Contains("séance", editor.ValueLabel);

        editor.FirstName = "Samir";
        editor.LastName = "Haddad";
        editor.Phone = "0770 12 34 56";
        editor.SelectedSubject = editor.SubjectOptions[1];
        editor.CompensationValue = "2500";
        await editor.ConfirmCommand.ExecuteAsync(null);
        await add;
        Assert.False(editor.HasError, editor.Error);

        var profile = host.Page<TeacherDetailViewModel>();
        Assert.Equal(editor.SavedId, profile.Id);
        Assert.Equal("Samir Haddad", profile.Name);
        Assert.StartsWith(editor.SubjectOptions[1].Label, profile.Subtitle);
        Assert.True(profile.HasNoGroups);
        Assert.Equal("Rien à payer", profile.PayState.Text);

        // Deactivate, then delete (no payments yet).
        var toggle = profile.ToggleActiveCommand.ExecuteAsync(null);
        await host.AnswerDialogAsync();
        await toggle;
        Assert.False(profile.IsActive);
        Assert.Equal("Inactif", profile.State.Text);
        var delete = profile.DeleteCommand.ExecuteAsync(null);
        await host.AnswerDialogAsync();
        await delete;
        Assert.IsType<TeachersViewModel>(nav.Current);
        Assert.Equal(9, host.Page<TeachersViewModel>().Rows.Count);
    }
}
