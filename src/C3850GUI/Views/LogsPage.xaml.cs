using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

public partial class LogsPage : SwitchPage
{
    private string _raw = "";
    private readonly DispatcherTimer _timer = new();

    public LogsPage()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => { if (Connected && IsLoaded && !App.Sessions.Busy) await SafeRefreshAsync(); };
        Unloaded += (_, _) => _timer.Stop();
        Loaded += (_, _) => { if (AutoRefresh.IsChecked == true) Start(); };
    }

    private void Start()
    {
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(5, App.Store.Settings.RefreshSeconds));
        _timer.Start();
    }

    private void Auto_Changed(object s, RoutedEventArgs e) { if (AutoRefresh.IsChecked == true) Start(); else _timer.Stop(); }

    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (RequireConnection()) await SafeRefreshAsync(); }

    protected override async Task RefreshAsync()
    {
        _raw = (await Session!.RunAsync("show logging", default, TimeSpan.FromSeconds(60))).Output;
        Sub.Text = $"Last refresh {DateTime.Now:HH:mm:ss}  ·  {_raw.Split('\n').Length} lines";
        Apply();
    }

    private void Filter_Changed(object s, TextChangedEventArgs e) => Apply();

    private void Apply()
    {
        var f = FilterBox.Text.Trim();
        var atEnd = Log.VerticalOffset + Log.ViewportHeight >= Log.ExtentHeight - 4;
        Log.Text = f.Length == 0 ? _raw : string.Join('\n', _raw.Split('\n').Where(l => l.Contains(f, StringComparison.OrdinalIgnoreCase)));
        if (atEnd || f.Length > 0) Log.ScrollToEnd();
    }

    private async void Clear_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection() || !Dialogs.Confirm(this, "Clear logging", "clear logging\n\nThis empties the in-memory log buffer on the switch.", "Clear", true)) return;
        try { await Session!.RunInteractiveAsync("clear logging", ""); await SafeRefreshAsync(); }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private void Flaps_Click(object s, RoutedEventArgs e)
    {
        FilterBox.Text = "UPDOWN";
    }

    private async void ErrDisable_Click(object s, RoutedEventArgs e)
    {
        var r = await RunAsync("show errdisable recovery"); if (r == null) return;
        var r2 = await RunAsync("show interfaces status err-disabled");
        Dialogs.ShowText(this, "Err-disable", r.Output + "\n\n" + (r2?.Output ?? ""));
    }

    private async void Tech_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection() || !Dialogs.Confirm(this, "show tech-support", "This can take a minute or two and produces a very large output. It will be saved to a file.")) return;
        var dlg = new SaveFileDialog { Filter = "Text|*.txt", FileName = $"{Session!.Hostname}-tech-support-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dlg.ShowDialog() != true) return;
        var r = await RunAsync("show tech-support", TimeSpan.FromMinutes(6)); if (r == null) return;
        File.WriteAllText(dlg.FileName, r.Output);
        Toast($"Saved {dlg.FileName}", ControlAppearance.Success, 6);
    }

    private void Save_Click(object s, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Text|*.txt", FileName = $"{Session?.Hostname ?? "switch"}-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, Log.Text);
        Toast($"Saved {dlg.FileName}", ControlAppearance.Success);
    }
}
