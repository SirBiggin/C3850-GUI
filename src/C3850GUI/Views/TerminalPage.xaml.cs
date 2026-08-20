using System.IO;
using System.Windows;
using System.Windows.Controls;
using C3850GUI.Controls;
using C3850GUI.Services;
using Microsoft.Win32;

namespace C3850GUI.Views;

public partial class TerminalPage : SwitchPage
{
    // One terminal surface per session so each keeps its own scrollback.
    private readonly Dictionary<SshSession, TerminalControl> _terms = new();
    private TerminalControl? _current;
    private bool _suppress;

    public TerminalPage()
    {
        InitializeComponent();
        SessionPicker.ItemsSource = App.Sessions.Open;
        App.Sessions.Open.CollectionChanged += (_, _) => Attach(App.Sessions.Active);
        Attach(App.Sessions.Active);
        Loaded += (_, _) => _current?.Focus();
    }

    protected override void OnActiveChanged() => Attach(App.Sessions.Active);

    private TerminalControl TermFor(SshSession s)
    {
        if (_terms.TryGetValue(s, out var t)) return t;
        t = new TerminalControl
        {
            FontFamilyName = App.Store.Settings.TerminalFont,
            TerminalFontSize = App.Store.Settings.TerminalFontSize
        };
        t.Input += s.Write;
        s.DataReceived += t.Feed;
        s.Disconnected += msg => t.Dispatcher.BeginInvoke(() => t.WriteLocal($"\r\n*** {msg} ***"));
        t.WriteLocal($"*** Attached to {s.Profile.Name} ({s.Profile.Host}) ***");
        _terms[s] = t;
        return t;
    }

    private void Attach(SshSession? s)
    {
        _suppress = true;
        SessionPicker.SelectedItem = s;
        _suppress = false;
        foreach (var dead in _terms.Keys.Where(k => !k.IsConnected && k != s).ToList()) _terms.Remove(dead);
        if (s == null) { _current = Term; return; }
        var t = TermFor(s);
        if (ReferenceEquals(t, _current)) return;
        TermHost.Child = t;
        _current = t;
        t.FontFamilyName = App.Store.Settings.TerminalFont;
        t.TerminalFontSize = App.Store.Settings.TerminalFontSize;
        if (IsLoaded) t.Focus();
    }

    private void SessionPicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || SessionPicker.SelectedItem is not SshSession s) return;
        App.Sessions.Active = s;
    }

    private void Clear_Click(object s, RoutedEventArgs e) => _current?.Clear();
    private void Break_Click(object s, RoutedEventArgs e) => Session?.Write("\x03");
    private void CtrlZ_Click(object s, RoutedEventArgs e) => Session?.Write("\x1a");

    private void Save_Click(object s, RoutedEventArgs e)
    {
        if (_current == null) return;
        var dlg = new SaveFileDialog { Filter = "Text|*.txt", FileName = $"{Session?.Hostname ?? "terminal"}-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, _current.AllText);
        Toast($"Saved {dlg.FileName}", Wpf.Ui.Controls.ControlAppearance.Success);
    }
}
