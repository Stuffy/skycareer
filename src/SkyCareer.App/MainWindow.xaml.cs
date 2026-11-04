using System.Windows;
using SkyCareer.Localization;

namespace SkyCareer.App;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        WebView.Services = services;
        // The loading screen in index.html runs before Blazor; it picks its texts from this (else the browser language).
        WebView.BlazorWebViewInitialized += (_, e) =>
        {
            WebViewDisplayRecovery.Attach(e.WebView);
            _ = e.WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.skyLang = '{L.Language}';");
        };
    }
}
