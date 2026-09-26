using System.Diagnostics;
using System.Runtime.InteropServices;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Desktop.Services;

public sealed class WindowsClipboard : IClipboard
{
    public void SetText(string text)
    {
        try
        {
            // Single call: WPF already retries briefly when another application holds the clipboard.
            System.Windows.Clipboard.SetDataObject(text, copy: true);
        }
        catch (COMException)
        {
            throw new BusinessException("Le presse-papiers est occupé par une autre application. Réessayez.");
        }
    }
}

public sealed class UrlLauncher : ILauncher
{
    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new BusinessException("Lien invalide : " + url);
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new BusinessException("Impossible d'ouvrir le lien (navigateur ou WhatsApp introuvable) : " + ex.Message);
        }
    }
}
