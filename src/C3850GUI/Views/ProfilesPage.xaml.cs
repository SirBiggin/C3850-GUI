using System.Windows;
using System.Windows.Controls;
using C3850GUI.Models;
using C3850GUI.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

public partial class ProfilesPage : SwitchPage
{
    private SwitchProfile? _editing;
    private bool _isNew;

    public ProfilesPage()
    {
        InitializeComponent();
        List.ItemsSource = App.Store.Profiles;
        if (App.Store.Profiles.Count > 0) List.SelectedIndex = 0;
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is SwitchProfile p) Load(p, false);
    }

    private void Load(SwitchProfile p, bool isNew)
    {
        _editing = p; _isNew = isNew;
        Editor.IsEnabled = true;
        NameBox.Text = p.Name; HostBox.Text = p.Host; PortBox.Value = p.Port; UserBox.Text = p.Username;
        AuthPw.IsChecked = p.Auth == AuthMode.Password; AuthKey.IsChecked = p.Auth == AuthMode.PrivateKey;
        ProtoSsh.IsChecked = p.Protocol == Protocol.Ssh; ProtoTelnet.IsChecked = p.Protocol == Protocol.Telnet;
        PwBox.Password = Protector.Unprotect(p.ProtectedPassword);
        EnableBox.Password = Protector.Unprotect(p.ProtectedEnableSecret);
        KeyBox.Text = p.PrivateKeyPath; KeyPassBox.Password = Protector.Unprotect(p.ProtectedKeyPassphrase);
        NotesBox.Text = p.Notes;
        GroupBox.Value = p.PortGroupSize;
        ColorBox.SelectedValue = p.AccentHex;
        if (ColorBox.SelectedIndex < 0) ColorBox.SelectedIndex = 0;
    }

    private SwitchProfile? Collect()
    {
        if (_editing == null) return null;
        if (string.IsNullOrWhiteSpace(HostBox.Text)) { Toast("Host / IP is required.", ControlAppearance.Caution); return null; }
        if (string.IsNullOrWhiteSpace(UserBox.Text)) { Toast("Username is required.", ControlAppearance.Caution); return null; }
        var p = _editing;
        p.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? HostBox.Text.Trim() : NameBox.Text.Trim();
        p.Host = HostBox.Text.Trim();
        p.Port = (int)(PortBox.Value ?? 22);
        p.Username = UserBox.Text.Trim();
        p.Auth = AuthKey.IsChecked == true ? AuthMode.PrivateKey : AuthMode.Password;
        p.Protocol = ProtoTelnet.IsChecked == true ? Protocol.Telnet : Protocol.Ssh;
        p.ProtectedPassword = Protector.Protect(PwBox.Password);
        p.ProtectedEnableSecret = Protector.Protect(EnableBox.Password);
        p.PrivateKeyPath = KeyBox.Text.Trim();
        p.ProtectedKeyPassphrase = Protector.Protect(KeyPassBox.Password);
        p.Notes = NotesBox.Text;
        p.PortGroupSize = (int)(GroupBox.Value ?? 12);
        p.AccentHex = ColorBox.SelectedValue?.ToString() ?? "#2E8BFF";
        return p;
    }

    private bool SaveCore()
    {
        var p = Collect(); if (p == null) return false;
        if (_isNew) { App.Store.Profiles.Add(p); _isNew = false; List.SelectedItem = p; }
        App.Store.Save();
        Toast($"Saved profile '{p.Name}'", ControlAppearance.Success, 2);
        return true;
    }

    private void Save_Click(object s, RoutedEventArgs e) => SaveCore();

    private async void SaveConnect_Click(object s, RoutedEventArgs e)
    {
        if (!SaveCore() || _editing == null) return;
        App.Store.Settings.LastProfileId = _editing.Id;
        await Main.ConnectAsync(_editing);
        if (App.Sessions.IsConnected) Main.NavigateTo(typeof(DashboardPage));
    }

    private async void Test_Click(object s, RoutedEventArgs e)
    {
        var p = Collect(); if (p == null) return;
        var btn = (Wpf.Ui.Controls.Button)s; btn.IsEnabled = false;
        try
        {
            using var test = new SshSession(p.Clone());
            await test.ConnectAsync();
            var r = await test.RunAsync("show version | include uptime");
            Toast($"OK — {r.Output.Trim()}", ControlAppearance.Success, 6);
        }
        catch (Exception ex) { Toast($"Failed: {ex.Message}", ControlAppearance.Danger, 8); }
        finally { btn.IsEnabled = true; }
    }

    private void New_Click(object s, RoutedEventArgs e)
    {
        List.SelectedItem = null;
        Load(new SwitchProfile { Name = "", Host = "", Username = Environment.UserName }, true);
        NameBox.Focus();
    }

    private void Dup_Click(object s, RoutedEventArgs e)
    {
        if (List.SelectedItem is not SwitchProfile p) return;
        var c = p.Clone(); c.Id = Guid.NewGuid(); c.Name = p.Name + " (copy)";
        List.SelectedItem = null;
        Load(c, true);
    }

    private void Delete_Click(object s, RoutedEventArgs e)
    {
        if (List.SelectedItem is not SwitchProfile p) return;
        if (!Dialogs.Confirm(this, "Delete profile", $"Delete '{p.Name}' ({p.Host})?", "Delete", true)) return;
        var open = App.Sessions.Get(p); if (open != null) App.Sessions.Disconnect(open);
        App.Store.Profiles.Remove(p);
        App.Store.Save();
        Editor.IsEnabled = false; _editing = null;
    }

    private void Proto_Changed(object s, RoutedEventArgs e)
    {
        if (PortBox == null || AuthKey == null) return;
        // swap the default port when switching protocols, keep custom ports alone
        if (ProtoTelnet.IsChecked == true) { if (PortBox.Value == 22) PortBox.Value = 23; AuthPw.IsChecked = true; AuthKey.IsEnabled = false; }
        else { if (PortBox.Value == 23) PortBox.Value = 22; AuthKey.IsEnabled = true; }
    }

    private void Auth_Changed(object s, RoutedEventArgs e)
    {
        if (PwPanel == null) return;
        PwPanel.Visibility = AuthPw.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        KeyPanel.Visibility = AuthKey.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseKey_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Private key", InitialDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"), Filter = "All files|*.*" };
        if (dlg.ShowDialog() == true) KeyBox.Text = dlg.FileName;
    }
}
