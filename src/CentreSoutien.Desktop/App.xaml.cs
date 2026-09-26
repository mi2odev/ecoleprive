using System.Globalization;
using System.IO;
using System.Windows.Markup;
using System.Windows;
using System.Windows.Threading;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Desktop.Services;
using CentreSoutien.Infrastructure;
using CentreSoutien.Infrastructure.Data;
using CentreSoutien.Infrastructure.Storage;
using CentreSoutien.Presentation;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CentreSoutien.Desktop;

public partial class App
{
    private IHost? _host;
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // French formatting everywhere: "7,5", dates dd/MM/yyyy in date pickers, binding conversions.
        var fr = new CultureInfo("fr-FR");
        fr.NumberFormat.NumberGroupSeparator = " ";
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = fr;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = fr;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(fr.IetfLanguageTag)));

        // One instance per Windows session: two copies writing the same database would be unsafe.
        _singleInstance = new Mutex(true, @"Local\CentreSoutien.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("L'application est déjà ouverte sur ce poste.", "Centre de soutien", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception ex) ErrorLog.Write(ex); };
        TaskScheduler.UnobservedTaskException += (_, args) => { ErrorLog.Write(args.Exception); args.SetObserved(); };

        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
            builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
            var storage = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();

            builder.Services.AddLocalInfrastructure(storage);
            builder.Services.AddPresentation();
            builder.Services.AddSingleton<IThemeService, ThemeService>();
            builder.Services.AddSingleton<IFilePicker, FilePicker>();
            builder.Services.AddSingleton<IShell, WindowsShell>();
            builder.Services.AddSingleton<IPrintService, PrintService>();
            builder.Services.AddSingleton<ActivityMonitor>();
            builder.Services.AddSingleton<MainWindow>();
            _host = builder.Build();

            var sp = _host.Services;
            ErrorLog.Folder = sp.GetRequiredService<AppPaths>().Logs;
            await sp.GetRequiredService<DatabaseInitializer>().InitializeAsync();

            var shell = sp.GetRequiredService<ShellViewModel>();
            await shell.InitializeAsync();
            var window = sp.GetRequiredService<MainWindow>();
            window.DataContext = shell;
            MainWindow = window;
            window.Show();
            sp.GetRequiredService<ActivityMonitor>().Start();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show("Impossible de démarrer l'application :\n\n" + ex.Message, "Centre de soutien", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write(e.Exception);
        var message = e.Exception is BusinessException ? e.Exception.Message : "Une erreur inattendue est survenue : " + e.Exception.Message;
        _host?.Services.GetService<INotifier>()?.Error(message);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Closing the application ends the owner's session.
        _host?.Services.GetService<AppSession>()?.SignOut();
        _host?.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
