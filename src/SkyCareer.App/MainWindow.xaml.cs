using System.Windows;

namespace SkyCareer.App;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        WebView.Services = services;
    }
}
