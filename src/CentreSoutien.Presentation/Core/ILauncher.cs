namespace CentreSoutien.Presentation.Core;

/// <summary>Opens web links with the default browser or registered application (e.g. wa.me links → WhatsApp Desktop).</summary>
public interface ILauncher
{
    void OpenUrl(string url);
}
