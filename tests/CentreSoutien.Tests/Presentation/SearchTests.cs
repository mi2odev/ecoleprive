using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Infrastructure.Data;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Pages;
using CentreSoutien.Presentation.ViewModels.Shell;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Tests.Presentation;

public class SearchTests
{
    private static async Task<Student> AddBenaliAsync(TestHost host) =>
        await host.Get<IStudentService>().SaveAsync(new Student { FirstName = "Rachid", LastName = "Bénali", Level = "3AS", Phone = "0799 88 77 66" });

    [Fact]
    public async Task Search_is_accent_and_case_insensitive_multi_word_and_ranked()
    {
        await using var host = await UiHost.CreateAsync();
        var rachid = await AddBenaliAsync(host);
        var search = host.Get<ISearchService>();

        var hits = await search.SearchAsync("benali");
        Assert.Contains(hits, h => h.Category == SearchCategory.Student && h.Id == rachid.Id && h.Title == "Rachid Bénali");
        Assert.True(hits.Count(h => h.Category == SearchCategory.Student) <= 8);

        // Several words must all match, in any order; the exact name comes first.
        hits = await search.SearchAsync("BÉNALI  rachid");
        var first = hits.First(h => h.Category == SearchCategory.Student);
        Assert.Equal(rachid.Id, first.Id);
        Assert.Equal(0, first.Rank);
        Assert.Single(hits, h => h.Category == SearchCategory.Student);
        Assert.Empty(await search.SearchAsync("rachid zzzz"));

        // Demo "Inès" found without typing the accent.
        Assert.Contains(await search.SearchAsync("ines"), h => h.Category == SearchCategory.Student && h.Title.StartsWith("Inès"));

        // Matricule, student phone typed without spaces, parent phone.
        Assert.Equal(rachid.Id, (await search.SearchAsync(rachid.Matricule)).First(h => h.Category == SearchCategory.Student).Id);
        Assert.Equal(rachid.Id, (await search.SearchAsync("0799887766")).First(h => h.Category == SearchCategory.Student).Id);
        var parent = (await host.Get<IParentService>().ListAsync()).First(p => p.Phone is not null);
        hits = await search.SearchAsync(parent.Phone!.Replace(" ", ""));
        Assert.Contains(hits, h => h.Category == SearchCategory.Parent && h.Id == parent.Id);
        Assert.Contains(hits, h => h.Category == SearchCategory.Student); // children found by their parent's phone

        // Teachers by subject, groups by course and level, receipts by number.
        Assert.Contains(await search.SearchAsync("boudiaf"), h => h.Category == SearchCategory.Teacher && h.Title == "Karima Boudiaf");
        Assert.Contains(await search.SearchAsync("histoire geographie"), h => h.Category == SearchCategory.Teacher);
        var group = (await search.SearchAsync("maths 3as a")).First(h => h.Category == SearchCategory.Group);
        Assert.Equal("Mathématiques · 3AS A", group.Title);
        Assert.NotNull(group.RelatedId);

        await using var db = await host.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var receipt = await db.StudentPayments.FirstAsync();
        var receiptHit = Assert.Single(await search.SearchAsync(receipt.ReceiptNumber), h => h.Category == SearchCategory.Receipt);
        Assert.Equal(receipt.StudentId, receiptHit.RelatedId);

        Assert.Empty(await search.SearchAsync("   "));
    }

    [Fact]
    public async Task Documents_are_found_by_title()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var file = Path.Combine(host.Folder, "reglement.txt");
        await File.WriteAllTextAsync(file, "règlement");
        await host.Get<IDocumentService>().AddAsync(file, "Règlement intérieur", "Autre", Domain.Enums.DocumentOwnerType.Center, null);
        var hit = Assert.Single(await host.Get<ISearchService>().SearchAsync("reglement interieur"));
        Assert.Equal(SearchCategory.Document, hit.Category);
        Assert.Equal("Règlement intérieur", hit.Title);
    }

    [Fact]
    public async Task Palette_searches_after_typing_and_opens_the_selected_result()
    {
        await using var host = await UiHost.CreateAsync();
        var rachid = await AddBenaliAsync(host);
        var shell = host.Get<ShellViewModel>();
        var nav = host.Get<Navigator>();
        var palette = shell.Search;

        shell.OpenSearchCommand.Execute(null);
        Assert.True(palette.IsOpen);
        Assert.True(palette.ShowHint);

        palette.Query = "benali";
        await palette.Pending;
        Assert.True(palette.HasResults);
        Assert.Equal("Élèves", palette.Groups[0].Label);
        Assert.Same(palette.Results[0], palette.Selected);
        Assert.True(palette.Selected!.IsSelected);

        // Keyboard selection wraps around.
        palette.MoveUpCommand.Execute(null);
        Assert.Same(palette.Results[^1], palette.Selected);
        Assert.False(palette.Results[0].IsSelected);
        palette.MoveDownCommand.Execute(null);
        Assert.Same(palette.Results[0], palette.Selected);

        palette.Query = "bénali rachid";
        await palette.Pending;
        Assert.Equal(rachid.Id, palette.Selected!.Hit.Id);
        await palette.ActivateCommand.ExecuteAsync(null);
        Assert.False(palette.IsOpen);
        Assert.Equal("", palette.Query);
        Assert.Equal("Rachid Bénali", host.Page<StudentDetailViewModel>().Name);

        // A group opens its course page with the group selected.
        palette.Open("maths 3as a");
        await palette.Pending;
        var group = palette.Results.First(r => r.Hit.Category == SearchCategory.Group);
        await group.OpenCommand.ExecuteAsync(null);
        var course = host.Page<CourseDetailViewModel>();
        Assert.Equal(group.Hit.RelatedId, ((CourseDetailViewModel.Target)course.LastParameter!).CourseId);
        Assert.Equal(group.Hit.Id, ((CourseDetailViewModel.Target)course.LastParameter!).GroupId);

        // A receipt opens the student's profile; a parent opens the parents page on that parent.
        await using var db = await host.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var receipt = await db.StudentPayments.Include(p => p.Student).FirstAsync();
        palette.Open(receipt.ReceiptNumber);
        await palette.Pending;
        await palette.Results.First(r => r.Hit.Category == SearchCategory.Receipt).OpenCommand.ExecuteAsync(null);
        Assert.Equal(receipt.Student!.FullName, host.Page<StudentDetailViewModel>().Name);

        var parent = (await host.Get<IParentService>().ListAsync()).First();
        palette.Open(parent.FullName);
        await palette.Pending;
        await palette.Results.First(r => r.Hit.Category == SearchCategory.Parent && r.Hit.Id == parent.Id).OpenCommand.ExecuteAsync(null);
        Assert.Equal(parent.Id, host.Page<ParentsViewModel>().LastParameter);
        Assert.False(nav.Current!.HasError, nav.Current.Error);

        // No result.
        palette.Open("zzzzzz");
        await palette.Pending;
        Assert.True(palette.ShowNoResult);
        Assert.False(palette.HasResults);
        palette.CloseCommand.Execute(null);
    }

    [Fact]
    public async Task Ctrl_K_palette_state_in_the_shell()
    {
        await using var host = await UiHost.CreateAsync();
        var shell = host.Get<ShellViewModel>();
        var session = host.Get<AppSession>();
        Assert.True(shell.IsAppInteractive);

        // Ctrl+K opens the palette; the page behind cannot take focus.
        shell.OpenSearchCommand.Execute(null);
        Assert.True(shell.Search.IsOpen);
        Assert.False(shell.IsAppInteractive);
        shell.CloseSearchCommand.Execute(null);
        Assert.False(shell.Search.IsOpen);
        Assert.True(shell.IsAppInteractive);

        // Enter in the header box opens the palette with the typed text.
        shell.SearchText = "  yacine ";
        shell.SearchCommand.Execute(null);
        Assert.True(shell.Search.IsOpen);
        Assert.Equal("yacine", shell.Search.Query);
        Assert.Equal("", shell.SearchText);
        await shell.Search.Pending;
        Assert.Contains(shell.Search.Results, r => r.Title.StartsWith("Yacine"));

        // Locking closes the palette, and it cannot be opened while locked.
        shell.LockNowCommand.Execute(null);
        Assert.False(shell.Search.IsOpen);
        Assert.Equal("", shell.Search.Query);
        shell.OpenSearchCommand.Execute(null);
        Assert.False(shell.Search.IsOpen);
        Assert.False(shell.IsAppInteractive);

        // Signing out closes it too.
        session.Unlock();
        shell.OpenSearchCommand.Execute("abc");
        Assert.True(shell.Search.IsOpen);
        session.SignOut();
        Assert.False(shell.Search.IsOpen);
    }
}
