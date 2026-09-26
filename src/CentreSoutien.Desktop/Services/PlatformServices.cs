using System.Diagnostics;
using System.IO;
using System.Windows;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using Microsoft.Win32;

namespace CentreSoutien.Desktop.Services;

/// <summary>Swaps the colour dictionary (first merged dictionary of App.xaml) between Light and Dark.</summary>
public sealed class ThemeService : IThemeService
{
    public AppTheme Current { get; private set; } = AppTheme.Light;

    public void Apply(AppTheme theme)
    {
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{theme}.xaml", UriKind.Absolute) };
        if (dictionaries.Count > 0) dictionaries[0] = palette;
        else dictionaries.Insert(0, palette);
        Current = theme;
    }
}

public sealed class FilePicker : IFilePicker
{
    public string? OpenFile(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dlg.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dlg.FileName : null;
    }

    public string? SaveFile(string title, string defaultName, string filter)
    {
        var dlg = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, OverwritePrompt = true };
        return dlg.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dlg.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        return dlg.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dlg.FolderName : null;
    }
}

public sealed class WindowsShell : IShell
{
    public void Open(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new Application.Abstractions.BusinessException("Fichier introuvable : " + path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void Reveal(string path)
    {
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
    }
}
