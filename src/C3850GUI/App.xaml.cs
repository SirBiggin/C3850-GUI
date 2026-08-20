using System.Windows;
using System.Windows.Threading;
using C3850GUI.Services;
using Wpf.Ui.Appearance;

namespace C3850GUI;

public partial class App : Application
{
    public static ProfileStore Store { get; } = new();
    public static SessionManager Sessions { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Store.Load();
        ApplicationThemeManager.Apply(Store.Settings.Theme == "Light" ? ApplicationTheme.Light : ApplicationTheme.Dark);
        DispatcherUnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += (_, ev) => ev.SetObserved();
        var w = new MainWindow();
        MainWindow = w;
        w.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show(e.Exception.Message, "C3850 GUI — unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Sessions.DisconnectAll();
        try { Store.Save(); } catch { }
        base.OnExit(e);
    }
}
