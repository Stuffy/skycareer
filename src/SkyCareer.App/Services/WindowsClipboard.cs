using SkyCareer.Localization;

namespace SkyCareer.App.Services;

/// <summary>Puts text on the Windows clipboard via WPF (the WebView's clipboard API needs a user gesture).</summary>
public static class WindowsClipboard
{
    public static async Task SetTextAsync(string text)
    {
        try
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => System.Windows.Clipboard.SetText(text));
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            throw new InvalidOperationException(L.T("The clipboard is busy right now – please try again."), ex);
        }
    }
}
