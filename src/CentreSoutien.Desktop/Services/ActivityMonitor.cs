using System.Windows.Input;
using System.Windows.Threading;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Shell;

namespace CentreSoutien.Desktop.Services;

/// <summary>
/// Feeds keyboard/mouse activity to the session (automatic lock), refreshes the header clock and
/// runs the daily automatic backup.
/// </summary>
public sealed class ActivityMonitor(AppSession session, ShellViewModel shell, IBackupService backup, INotifier notifier, TimeProvider clock)
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private DateTime _lastBackupCheck = DateTime.MinValue;
    private bool _backupRunning;

    public void Start()
    {
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            if (e.StagingItem.Input is KeyboardEventArgs or MouseButtonEventArgs or MouseWheelEventArgs
                || (e.StagingItem.Input is MouseEventArgs && e.StagingItem.Input.RoutedEvent == Mouse.MouseMoveEvent))
                session.Touch();
        };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
        shell.UpdateClock();
    }

    private async Task TickAsync()
    {
        session.Tick();
        shell.UpdateClock();

        var now = clock.GetLocalNow().DateTime;
        if (_backupRunning || now - _lastBackupCheck < TimeSpan.FromMinutes(1) || !session.IsSignedIn) return;
        _lastBackupCheck = now;
        _backupRunning = true;
        try
        {
            if (await backup.RunScheduledAsync(now)) notifier.Info("Sauvegarde automatique effectuée");
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            _lastBackupCheck = now.AddMinutes(30); // retry later instead of every minute
            notifier.Error("La sauvegarde automatique a échoué : " + ex.Message);
        }
        finally
        {
            _backupRunning = false;
        }
    }
}
