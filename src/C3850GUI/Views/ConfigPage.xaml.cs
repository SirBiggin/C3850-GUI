using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using C3850GUI.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

public partial class ConfigPage : SwitchPage
{
    public ConfigPage() { InitializeComponent(); }

    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (RequireConnection()) await SafeRefreshAsync(); }

    protected override async Task RefreshAsync()
    {
        Running.Text = (await Session!.RunAsync("show running-config", default, TimeSpan.FromSeconds(90))).Output;
        Startup.Text = (await Session!.RunAsync("show startup-config", default, TimeSpan.FromSeconds(90))).Output;
        var dirty = Normalize(Running.Text) != Normalize(Startup.Text);
        Sub.Text = dirty ? "⚠ Running config differs from startup — unsaved changes." : "Running config matches startup config.";
    }

    private static string Normalize(string cfg) => string.Join('\n', cfg.Split('\n')
        .Select(l => l.TrimEnd())
        .Where(l => !l.StartsWith("!") && !l.StartsWith("Building configuration") && !l.StartsWith("Current configuration") && !l.StartsWith("Using ") && !l.StartsWith("ntp clock-period") && l.Length > 0));

    private async void Save_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection()) return;
        try
        {
            var r = await Session!.RunInteractiveAsync("write memory", "", default, TimeSpan.FromSeconds(60));
            var ok = r.Output.Contains("[OK]");
            Toast(ok ? "Configuration saved to NVRAM" : r.Output, ok ? ControlAppearance.Success : ControlAppearance.Caution, 6);
            if (ok) await SafeRefreshAsync();
        }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private async void Backup_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection()) return;
        if (Running.Text.Length == 0) await SafeRefreshAsync();
        var dir = Path.Combine(ProfileStore.BackupDir, SafeName(Session!.Profile.Name));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{Session.Hostname}-running-{DateTime.Now:yyyyMMdd-HHmmss}.cfg");
        File.WriteAllText(file, Running.Text);
        Toast($"Backed up to {file}", ControlAppearance.Success, 6);
    }

    private static string SafeName(string n) => string.Concat(n.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private void OpenBackups_Click(object s, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ProfileStore.BackupDir);
        Process.Start(new ProcessStartInfo("explorer.exe", ProfileStore.BackupDir) { UseShellExecute = true });
    }

    private void Diff_Click(object s, RoutedEventArgs e)
    {
        Result.Text = Diff(Startup.Text, Running.Text, "startup-config", "running-config");
        SelectResultTab();
    }

    private void DiffFile_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Config|*.cfg;*.txt|All|*.*", InitialDirectory = ProfileStore.BackupDir };
        if (dlg.ShowDialog() != true) return;
        Result.Text = Diff(File.ReadAllText(dlg.FileName), Running.Text, Path.GetFileName(dlg.FileName), "running-config");
        SelectResultTab();
    }

    private void SelectResultTab()
    {
        if (Result.Parent is TabItem ti && ti.Parent is TabControl tc) tc.SelectedItem = ti;
    }

    /// <summary>Simple line-based LCS diff, good enough for configs.</summary>
    private static string Diff(string a, string b, string nameA, string nameB)
    {
        var A = Normalize(a).Split('\n'); var B = Normalize(b).Split('\n');
        int n = A.Length, m = B.Length;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = A[i] == B[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- {nameA}").AppendLine($"+++ {nameB}").AppendLine();
        int x = 0, y = 0, changes = 0;
        while (x < n && y < m)
        {
            if (A[x] == B[y]) { x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) { sb.AppendLine("- " + A[x++]); changes++; }
            else { sb.AppendLine("+ " + B[y++]); changes++; }
        }
        while (x < n) { sb.AppendLine("- " + A[x++]); changes++; }
        while (y < m) { sb.AppendLine("+ " + B[y++]); changes++; }
        if (changes == 0) sb.AppendLine("(no differences)");
        return sb.ToString();
    }

    private async void Push_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection()) return;
        var w = new Wpf.Ui.Controls.FluentWindow { Title = "Push configuration", Width = 720, Height = 520, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, ExtendsContentIntoTitleBar = true, WindowBackdropType = WindowBackdropType.Mica };
        var tb = new System.Windows.Controls.TextBox { AcceptsReturn = true, AcceptsTab = true, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 8) };
        var ok = new Wpf.Ui.Controls.Button { Content = "Send in configure terminal", Appearance = ControlAppearance.Primary, HorizontalAlignment = HorizontalAlignment.Right };
        var root = new DockPanel { Margin = new Thickness(20, 12, 20, 16) };
        DockPanel.SetDock(ok, Dock.Bottom);
        var title = new System.Windows.Controls.TextBlock { Text = "Paste configuration lines (as you would type them after 'configure terminal'). 'end' is added for you.", TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title); root.Children.Add(ok); root.Children.Add(tb);
        w.Content = root;
        ok.Click += (_, _) => { w.DialogResult = true; w.Close(); };
        if (w.ShowDialog() != true || string.IsNullOrWhiteSpace(tb.Text)) return;
        var lines = tb.Text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('!')).ToArray();
        if (await ConfigureAsync($"Push {lines.Length} line(s)", lines)) await SafeRefreshAsync();
        Result.Text = App.Sessions.Activity.FirstOrDefault()?.Output ?? "";
    }

    private void Copy_Click(object s, RoutedEventArgs e)
    {
        var tc = (TabControl)((TabItem)Running.Parent).Parent;
        var tb = tc.SelectedIndex switch { 0 => Running, 1 => Startup, _ => Result };
        if (tb.Text.Length > 0) { Clipboard.SetText(tb.Text); Toast("Copied", ControlAppearance.Success, 2); }
    }

    private async void Archive_Click(object s, RoutedEventArgs e)
    {
        var r = await RunAsync("show archive"); if (r == null) return;
        Dialogs.ShowText(this, "Archive", r.Output);
    }

    private void Find_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var tc = (TabControl)((TabItem)Running.Parent).Parent;
        var tb = tc.SelectedIndex switch { 0 => Running, 1 => Startup, _ => Result };
        var q = FindBox.Text; if (q.Length == 0) return;
        var start = tb.SelectionStart + tb.SelectionLength;
        var idx = tb.Text.IndexOf(q, start, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = tb.Text.IndexOf(q, 0, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) { Toast("Not found", ControlAppearance.Caution, 2); return; }
        tb.Focus(); tb.Select(idx, q.Length);
        tb.ScrollToLine(tb.GetLineIndexFromCharacterIndex(idx));
    }
}
