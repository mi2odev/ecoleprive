using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Tests.Presentation;

/// <summary>Backend + view models with fake platform services (no WPF). Demo data loaded and owner signed in.</summary>
public static class UiHost
{
    public static async Task<TestHost> CreateAsync(bool demo = true)
    {
        var host = await TestHost.CreateAsync(configure: sc => sc.AddPresentationWithFakes());
        if (demo) await host.Get<IDemoDataService>().SeedAsync();
        var session = host.Get<AppSession>();
        session.SignIn(await host.Get<IAuthService>().GetAccountAsync() is var a ? Unforced(a) : null!);
        return host;
    }

    public static IServiceCollection AddPresentationWithFakes(this IServiceCollection sc)
    {
        sc.AddPresentation();
        sc.AddSingleton<FakePlatform>();
        sc.AddSingleton<IFilePicker>(sp => sp.GetRequiredService<FakePlatform>());
        sc.AddSingleton<IShell>(sp => sp.GetRequiredService<FakePlatform>());
        sc.AddSingleton<IPrintService>(sp => sp.GetRequiredService<FakePlatform>());
        sc.AddSingleton<IThemeService>(sp => sp.GetRequiredService<FakePlatform>());
        return sc;
    }

    private static OwnerAccount Unforced(OwnerAccount a)
    {
        a.MustChangePassword = false;
        return a;
    }

    public static T Page<T>(this TestHost host) where T : PageViewModel => host.Get<Navigator>().Current as T
        ?? throw new InvalidOperationException($"Current page is {host.Get<Navigator>().Current?.GetType().Name}, expected {typeof(T).Name}");

    /// <summary>Confirms (or cancels) the dialog currently open, if any, and returns it.</summary>
    public static async Task<DialogViewModel?> AnswerDialogAsync(this TestHost host, bool confirm = true)
    {
        await Task.Yield();
        var d = host.Get<DialogHost>().Current;
        if (d is null) return null;
        if (confirm) await d.ConfirmCommand.ExecuteAsync(null);
        else d.Cancel();
        return d;
    }
}

public sealed class FakePlatform : IFilePicker, IShell, IPrintService, IThemeService
{
    public string? NextOpenFile { get; set; }
    public string? NextSaveFile { get; set; }
    public string? NextFolder { get; set; }
    public List<string> Opened { get; } = [];
    public List<string> Printed { get; } = [];
    /// <summary>Pages of every <see cref="PrintPages"/> job, in order.</summary>
    public List<(string Job, IReadOnlyList<PrintPage> Pages)> PrintedPages { get; } = [];

    public string? OpenFile(string title, string filter) => NextOpenFile;
    public string? SaveFile(string title, string defaultName, string filter) => NextSaveFile;
    public string? PickFolder(string title) => NextFolder;
    public void Open(string path) => Opened.Add(path);
    public void Reveal(string path) => Opened.Add(path);
    public void PrintPages(string jobName, CenterSettings settings, string? logoPath, IReadOnlyList<PrintPage> pages)
    {
        Printed.Add(jobName);
        PrintedPages.Add((jobName, pages));
    }
    public void PrintReceipt(StudentPayment payment, CenterSettings settings, string? logoPath) => Printed.Add(payment.ReceiptNumber);
    public void PrintReport(string title, string subtitle, CenterSettings settings, IReadOnlyList<(string Label, string Value)> summary, IReadOnlyList<PrintTable> tables) => Printed.Add(title);
    public AppTheme Current { get; private set; }
    public void Apply(AppTheme theme) => Current = theme;
}
