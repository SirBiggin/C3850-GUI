using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using C3850GUI.Services;
using Wpf.Ui.Appearance;

namespace C3850GUI.Views;

public partial class SettingsPage : SwitchPage
{
    private bool _init;
    private AppSettings S => App.Store.Settings;

    public SettingsPage()
    {
        InitializeComponent();
        ThemeBox.SelectedIndex = S.Theme == "Light" ? 1 : 0;
        var mono = Fonts.SystemFontFamilies.Select(f => f.Source).Where(n =>
            n.Contains("Mono", StringComparison.OrdinalIgnoreCase) || n.Contains("Consolas") || n.Contains("Courier") || n.Contains("Code", StringComparison.OrdinalIgnoreCase) || n.Contains("Fira") || n.Contains("JetBrains"))
            .OrderBy(n => n).ToList();
        if (!mono.Contains(S.TerminalFont)) mono.Insert(0, S.TerminalFont);
        FontBox.ItemsSource = mono;
        FontBox.Text = S.TerminalFont;
        FontSizeBox.Value = S.TerminalFontSize;
        ConfirmToggle.IsChecked = S.ConfirmConfigCommands;
        RefreshBox.Value = S.RefreshSeconds;
        DataPath.Text = $"Profiles and settings: {ProfileStore.FilePath}\nConfig backups: {ProfileStore.BackupDir}";
        _init = true;
    }

    private void Theme_Changed(object s, SelectionChangedEventArgs e)
    {
        if (!_init) return;
        S.Theme = ThemeBox.SelectedIndex == 1 ? "Light" : "Dark";
        ApplicationThemeManager.Apply(S.Theme == "Light" ? ApplicationTheme.Light : ApplicationTheme.Dark);
        App.Store.Save();
    }

    private void Font_Changed(object s, RoutedEventArgs e)
    {
        if (!_init || string.IsNullOrWhiteSpace(FontBox.Text)) return;
        S.TerminalFont = FontBox.Text.Trim(); App.Store.Save();
    }

    private void FontSize_Changed(object s, RoutedEventArgs e)
    {
        if (!_init || FontSizeBox.Value is not { } v) return;
        S.TerminalFontSize = v; App.Store.Save();
    }

    private void Confirm_Changed(object s, RoutedEventArgs e)
    {
        if (!_init) return;
        S.ConfirmConfigCommands = ConfirmToggle.IsChecked == true; App.Store.Save();
    }

    private void Refresh_Changed(object s, RoutedEventArgs e)
    {
        if (!_init || RefreshBox.Value is not { } v) return;
        S.RefreshSeconds = (int)v; App.Store.Save();
    }

    private void OpenData_Click(object s, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", ProfileStore.Dir) { UseShellExecute = true });
}
