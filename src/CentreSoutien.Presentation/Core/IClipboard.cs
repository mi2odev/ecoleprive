namespace CentreSoutien.Presentation.Core;

/// <summary>System clipboard (messages to paste into WhatsApp Desktop, SMS, e-mail…).</summary>
public interface IClipboard
{
    void SetText(string text);
}
