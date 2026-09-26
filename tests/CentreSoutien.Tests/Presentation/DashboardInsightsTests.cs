using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Pages;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Tests.Presentation;

public class DashboardInsightsTests
{
    private static Task Run(IRelayCommand command) => ((IAsyncRelayCommand)command).ExecuteAsync(null);

    private static async Task<DashboardViewModel> OpenAsync(TestHost host)
    {
        await host.Get<Navigator>().NavigateAsync<DashboardViewModel>();
        var page = host.Page<DashboardViewModel>();
        Assert.False(page.HasError, page.Error);
        return page;
    }

    [Fact]
    public async Task Dashboard_keeps_its_blocks_and_lists_prioritized_alerts()
    {
        await using var host = await UiHost.CreateAsync();
        var page = await OpenAsync(host);

        // Existing content is still there.
        Assert.Equal(6, page.Finance.Count);
        Assert.Equal(3, page.Blocks.Count);
        Assert.NotEmpty(page.Today);

        Assert.False(page.HasNoAlert);
        Assert.Equal($"{page.Alerts.Count} points", page.AlertCount);
        var severities = page.Alerts.Select(a => a.Badge.Kind switch { BadgeKind.Bad => 0, BadgeKind.Warn => 1, _ => 2 }).ToList();
        Assert.Equal(severities.Order(), severities);

        // Demo: September 26 is past the due day and several students still owe their fees.
        var unpaid = Assert.Single(page.Alerts, a => a.Kind == AlertKind.UnpaidStudents);
        Assert.Equal(BadgeKind.Bad, unpaid.Badge.Kind);
        Assert.InRange(unpaid.Items.Count, 1, 5);
        Assert.All(unpaid.Items, i => Assert.EndsWith("DZD", i.Value));
        Assert.True(unpaid.HasAction);

        // No backup was ever made in the demo database.
        var backup = Assert.Single(page.Alerts, a => a.Kind == AlertKind.BackupOverdue);
        Assert.Equal(BadgeKind.Warn, backup.Badge.Kind);
    }

    [Fact]
    public async Task Alert_links_open_the_right_pages()
    {
        await using var host = await UiHost.CreateAsync();
        var page = await OpenAsync(host);
        var unpaid = page.Alerts.Single(a => a.Kind == AlertKind.UnpaidStudents);

        await Run(unpaid.Items[0].Open);
        var student = host.Page<StudentDetailViewModel>();
        Assert.False(student.HasError, student.Error);
        Assert.NotEqual(0, student.Id);

        page = await OpenAsync(host);
        await Run(page.Alerts.Single(a => a.Kind == AlertKind.UnpaidStudents).Action!);
        Assert.Equal("Septembre 2026", host.Page<PaymentsViewModel>().MonthLabel);

        page = await OpenAsync(host);
        await Run(page.Alerts.Single(a => a.Kind == AlertKind.BackupOverdue).Action!);
        Assert.True(host.Page<SettingsViewModel>().IsBackup);

        page = await OpenAsync(host);
        if (page.Alerts.FirstOrDefault(a => a.Kind is AlertKind.GroupFull or AlertKind.GroupNearlyFull) is { } groups)
        {
            await Run(groups.Items[0].Open);
            Assert.False(host.Page<CourseDetailViewModel>().HasError);
        }
    }

    [Fact]
    public async Task Finished_session_without_attendance_is_reported_and_opens_its_sheet()
    {
        await using var host = await UiHost.CreateAsync();
        var page = await OpenAsync(host);
        Assert.DoesNotContain(page.Alerts, a => a.Kind == AlertKind.AttendanceMissing); // demo: attendance taken

        int sessionId;
        await using (var db = await host.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var today = new DateTime(2026, 9, 26);
            var session = (await db.Sessions.Include(s => s.Attendance).Where(s => s.Date == today).ToListAsync())
                .First(s => s.End <= TimeSpan.FromHours(13) && s.Attendance.Count > 0);
            db.Attendance.RemoveRange(session.Attendance);
            session.Status = SessionStatus.Planned;
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        page = await OpenAsync(host);
        var alert = Assert.Single(page.Alerts, a => a.Kind == AlertKind.AttendanceMissing);
        Assert.Equal(BadgeKind.Bad, alert.Badge.Kind);
        Assert.Single(alert.Items);
        await Run(alert.Action!);
        var attendance = host.Page<AttendanceViewModel>();
        Assert.Equal(sessionId, attendance.Selected?.Id);
    }

    [Fact]
    public async Task Absence_threshold_alert_links_to_the_student()
    {
        await using var host = await UiHost.CreateAsync();
        var settings = host.Get<ISettingsService>();
        var s = await settings.GetAsync();
        s.AbsenceAlertThreshold = 1;
        await settings.SaveAsync(s);

        var page = await OpenAsync(host);
        var alert = Assert.Single(page.Alerts, a => a.Kind == AlertKind.AbsenceThreshold);
        Assert.Equal(BadgeKind.Warn, alert.Badge.Kind);
        Assert.All(alert.Items, i => Assert.Contains("absence", i.Value));
        await Run(alert.Items[0].Open);
        Assert.IsType<StudentDetailViewModel>(host.Get<Navigator>().Current);
    }

    [Fact]
    public async Task Charts_cover_six_months_eight_weeks_and_levels()
    {
        await using var host = await UiHost.CreateAsync();
        var page = await OpenAsync(host);

        Assert.Equal(["Avr.", "Mai", "Juin", "Juil.", "Août", "Sept."], page.FinanceChart.Categories);
        Assert.Equal(["Recettes", "Rémunérations", "Dépenses", "Bénéfice estimé"], page.FinanceChart.Series.Select(x => x.Name));
        Assert.Equal(ChartSeriesKind.Line, page.FinanceChart.Series[^1].Kind);
        Assert.All(page.FinanceChart.Series, x => Assert.Equal(6, x.Values.Count));
        Assert.True(page.FinanceChart.Series[0].Values[^1] > 0);
        Assert.Equal(0, page.FinanceChart.Series[0].Values[0]); // nothing before the demo data
        Assert.Contains("Bénéfice estimé sur 6 mois", page.FinanceSummary);

        Assert.Equal(100, page.CollectionChart.Maximum);
        Assert.Equal(ChartValueFormat.Percent, page.CollectionChart.Format);
        Assert.InRange(page.CollectionChart.Series[0].Values[^1], 1, 100);
        Assert.True(double.IsNaN(page.CollectionChart.Series[0].Values[0])); // nothing expected in April
        Assert.StartsWith("Ce mois :", page.CollectionSummary);

        Assert.Equal(8, page.AttendanceChart.Categories.Count);
        Assert.Equal("26/09", page.AttendanceChart.Categories[^1]);
        Assert.InRange(page.AttendanceChart.Series[0].Values[^1], 50, 100);
        Assert.StartsWith("Moyenne sur 8 semaines", page.AttendanceSummary);

        Assert.Equal(["4AM", "2AS", "3AS"], page.LevelsChart.Categories);
        Assert.Equal(page.Blocks[0].Stats[1].Value, page.LevelsChart.Series[0].Values.Sum().ToString());
    }

    [Fact]
    public async Task Empty_center_shows_nothing_to_do_and_empty_charts()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        var settings = host.Get<ISettingsService>();
        var s = await settings.GetAsync();
        s.LastBackupAt = new DateTime(2026, 9, 26, 8, 0, 0);
        await settings.SaveAsync(s);

        var page = await OpenAsync(host);
        Assert.True(page.HasNoAlert);
        Assert.Empty(page.Alerts);
        Assert.Equal("", page.AlertCount);
        Assert.True(page.FinanceChart.IsEmpty);
        Assert.True(page.CollectionChart.IsEmpty);
        Assert.True(page.AttendanceChart.IsEmpty);
        Assert.True(page.LevelsChart.IsEmpty);
        Assert.Equal("Aucune mensualité attendue ce mois", page.CollectionSummary);
        Assert.Equal("Aucun appel enregistré sur la période", page.AttendanceSummary);
    }

    [Fact]
    public void Chart_data_formats_values_and_describes_categories()
    {
        var data = new ChartData(["Août", "Sept."],
        [
            new("Recettes", [12500, 0], ChartColor.Accent),
            new("Taux", [double.NaN, 87.5], ChartColor.Ok, ChartSeriesKind.Line),
        ], ChartValueFormat.Money);
        Assert.False(data.IsEmpty);
        Assert.Equal("12 500", data.FormatAxis(12500));
        Assert.Equal("Août" + Environment.NewLine + "Recettes : 12 500 DZD" + Environment.NewLine + "Taux : —", data.Describe(0));
        Assert.Equal("", data.Describe(5));
        Assert.Equal("87.5 %", data with { Format = ChartValueFormat.Percent } is var p ? p.FormatValue(87.5) : "");
        Assert.True(new ChartData(["Sept."], [new("Recettes", [0], ChartColor.Accent)]).IsEmpty);
        Assert.True(ChartData.Empty.IsEmpty);
    }
}
