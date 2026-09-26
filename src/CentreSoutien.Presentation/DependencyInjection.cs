using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation;

public static class DependencyInjection
{
    /// <summary>Registers the shell, navigation, dialogs and every page/dialog view model (transient).</summary>
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddSingleton<Navigator>();
        services.AddSingleton<INavigator>(sp => sp.GetRequiredService<Navigator>());
        services.AddSingleton<DialogHost>();
        services.AddSingleton<Notifier>();
        services.AddSingleton<INotifier>(sp => sp.GetRequiredService<Notifier>());
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<LockViewModel>();
        services.AddSingleton<ChangePasswordViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<ShellViewModel>();

        foreach (var type in typeof(DependencyInjection).Assembly.GetTypes())
        {
            if (type.IsAbstract || !type.IsClass) continue;
            if (typeof(PageViewModel).IsAssignableFrom(type) || (typeof(DialogViewModel).IsAssignableFrom(type) && type != typeof(ConfirmDialogViewModel)))
                services.AddTransient(type);
        }
        return services;
    }
}
