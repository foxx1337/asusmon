using AsusMon.Ui;
using Microsoft.UI.Xaml;

namespace AsusMon;

/// <summary>WinUI application shell, used only when launched without a console.</summary>
/// <remarks>
/// This type must live in the project root namespace. The XAML type-info
/// generator only attaches its <c>IXamlMetadataProvider</c> implementation to an
/// <c>App</c> class it finds there, and without that provider every XAML type
/// fails to resolve at runtime.
/// </remarks>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            GuiFailure.Report(e.Exception);
        };

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
