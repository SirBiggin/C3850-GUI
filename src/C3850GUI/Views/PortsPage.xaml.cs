using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using C3850GUI.Models;
using C3850GUI.Services;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

public partial class PortsPage : SwitchPage
{
    private List<PortInfo> _all = new();
    private readonly ObservableCollection<PortInfo> _view = new();

    public PortsPage()
    {
        InitializeComponent();
        Grid.ItemsSource = _view;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (RequireConnection()) await SafeRefreshAsync(); }

    protected override async Task RefreshAsync()
    {
        var r = await Session!.RunAsync("show interfaces status");
        _all = IosParser.InterfacesStatus(r.Output);
        ApplyFilter();
        // front panel: group physical ports by stack member ("Gi1/0/1" → member 1)
        var groups = _all.Where(p => Regex.IsMatch(p.Name, @"^\w+\d+/\d+/\d+$"))
            .GroupBy(p => "Switch " + Regex.Match(p.Name, @"(\d+)/").Groups[1].Value)
            .OrderBy(g => g.Key)
            .Select(g => new KeyValuePair<string, List<PortInfo>>(g.Key, g.OrderBy(p => PortSortKey(p.Name)).ToList()))
            .ToList();
        Panel.ItemsSource = groups;
    }

    private static long PortSortKey(string name)
    {
        var m = Regex.Match(name, @"(\d+)/(\d+)/(\d+)$");
        if (!m.Success) return long.MaxValue;
        var isTen = name.StartsWith("Te") || name.StartsWith("Fo") || name.StartsWith("Tw");
        return (isTen ? 1_000_000 : 0) + int.Parse(m.Groups[1].Value) * 10000 + int.Parse(m.Groups[2].Value) * 1000 + int.Parse(m.Groups[3].Value);
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var f = FilterBox.Text.Trim();
        _view.Clear();
        foreach (var p in _all)
            if (f.Length == 0 || $"{p.Name} {p.Description} {p.Status} {p.Vlan} {p.Type}".Contains(f, StringComparison.OrdinalIgnoreCase))
                _view.Add(p);
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (PortInfo p in e.RemovedItems) p.IsSelected = false;
        foreach (PortInfo p in e.AddedItems) p.IsSelected = true;
    }

    private void PortBox_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PortInfo p) return;
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) Grid.SelectedItems.Clear();
        if (!_view.Contains(p)) { FilterBox.Text = ""; }
        Grid.SelectedItems.Add(p);
        Grid.ScrollIntoView(p);
    }

    private List<PortInfo> Selected => Grid.SelectedItems.Cast<PortInfo>().ToList();

    private List<PortInfo>? NeedSelection()
    {
        var s = Selected;
        if (s.Count == 0) { Toast("Select one or more ports first.", ControlAppearance.Caution); return null; }
        return s;
    }

    /// <summary>Build "interface X / lines / exit" blocks for every selected port.</summary>
    private static string[] PerPort(IEnumerable<PortInfo> ports, params string[] lines)
    {
        var list = new List<string>();
        foreach (var p in ports) { list.Add($"interface {p.Name}"); list.AddRange(lines); list.Add("exit"); }
        return list.ToArray();
    }

    private async Task ApplyAsync(string what, params string[] lines)
    {
        var sel = NeedSelection(); if (sel == null) return;
        if (await ConfigureAsync($"{what} ({sel.Count} port{(sel.Count == 1 ? "" : "s")})", PerPort(sel, lines)))
            await SafeRefreshAsync();
    }

    private async void Enable_Click(object s, RoutedEventArgs e) => await ApplyAsync("Enable", "no shutdown");
    private async void Disable_Click(object s, RoutedEventArgs e) => await ApplyAsync("Disable", "shutdown");

    private async void Bounce_Click(object s, RoutedEventArgs e)
    {
        var sel = NeedSelection(); if (sel == null) return;
        if (!await ConfigureAsync($"Bounce ({sel.Count})", PerPort(sel, "shutdown"))) return;
        await Task.Delay(2000);
        if (!RequireConnection()) return;
        var r = await Session!.ConfigureAsync(PerPort(sel, "no shutdown"));
        Toast(r.Error ? r.ErrorText : "Bounce complete", r.Error ? ControlAppearance.Danger : ControlAppearance.Success);
        await SafeRefreshAsync();
    }

    private async void Desc_Click(object s, RoutedEventArgs e)
    {
        var sel = NeedSelection(); if (sel == null) return;
        var d = Dialogs.Prompt(this, "Port description", $"Description for {sel.Count} port(s) (empty clears):", sel[0].Description);
        if (d == null) return;
        await ApplyAsync("Set description", d.Trim().Length == 0 ? "no description" : $"description {d.Trim()}");
    }

    private async void AccessVlan_Click(object s, RoutedEventArgs e)
    {
        var sel = NeedSelection(); if (sel == null) return;
        var f = Dialogs.Form(this, "Access port", ("Access VLAN", sel[0].Vlan, "e.g. 20"), ("Voice VLAN (optional)", "", "e.g. 30"));
        if (f == null || !int.TryParse(f["Access VLAN"], out var v)) return;
        var lines = new List<string> { "switchport mode access", $"switchport access vlan {v}" };
        if (int.TryParse(f["Voice VLAN (optional)"], out var voice)) lines.Add($"switchport voice vlan {voice}");
        await ApplyAsync("Access VLAN", lines.ToArray());
    }

    private async void Trunk_Click(object s, RoutedEventArgs e)
    {
        var sel = NeedSelection(); if (sel == null) return;
        var f = Dialogs.Form(this, "Trunk port", ("Native VLAN", "1", ""), ("Allowed VLANs", "all", "e.g. 10,20,30-40 or all"));
        if (f == null) return;
        await ApplyAsync("Trunk", "switchport mode trunk", $"switchport trunk native vlan {f["Native VLAN"]}", $"switchport trunk allowed vlan {f["Allowed VLANs"]}");
    }

    private async void Poe_Click(object s, RoutedEventArgs e)
    {
        var sel = NeedSelection(); if (sel == null) return;
        var v = Dialogs.Prompt(this, "PoE mode", "power inline mode: auto | never | static | auto max <mW>", "auto");
        if (v == null) return;
        await ApplyAsync("PoE", $"power inline {v.Trim()}");
    }

    private async void SpeedDuplex_Click(object s, RoutedEventArgs e)
    {
        var sel = NeedSelection(); if (sel == null) return;
        var f = Dialogs.Form(this, "Speed / duplex", ("Speed", "auto", "auto | 10 | 100 | 1000 | 10000"), ("Duplex", "auto", "auto | full | half"));
        if (f == null) return;
        await ApplyAsync("Speed/duplex", $"speed {f["Speed"]}", $"duplex {f["Duplex"]}");
    }

    private async void Custom_Click(object s, RoutedEventArgs e)
    {
        var sel = NeedSelection(); if (sel == null) return;
        var v = Dialogs.Prompt(this, "Custom interface config", "Interface-mode lines, separated by ';'", "", "e.g. spanning-tree portfast; storm-control broadcast level 5");
        if (string.IsNullOrWhiteSpace(v)) return;
        await ApplyAsync("Custom config", v.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray());
    }

    private async Task ShowForSelected(string cmdFmt, string title)
    {
        var sel = NeedSelection(); if (sel == null) return;
        var sb = new System.Text.StringBuilder();
        foreach (var p in sel.Take(12))
        {
            var r = await RunAsync(string.Format(cmdFmt, p.Name));
            if (r == null) return;
            sb.AppendLine($"===== {p.Name} =====").AppendLine(r.Output).AppendLine();
        }
        Dialogs.ShowText(this, title, sb.ToString());
    }

    private async void Details_Click(object s, RoutedEventArgs e) => await ShowForSelected("show interfaces {0}", "Interface details");
    private async void RunConfig_Click(object s, RoutedEventArgs e) => await ShowForSelected("show running-config interface {0}", "Running config");
    private async void Counters_Click(object s, RoutedEventArgs e) => await ShowForSelected("show interfaces {0} counters errors", "Error counters");
    private async void Macs_Click(object s, RoutedEventArgs e) => await ShowForSelected("show mac address-table interface {0}", "MAC addresses");

    private async void ClearCounters_Click(object s, RoutedEventArgs e)
    {
        var sel = NeedSelection(); if (sel == null) return;
        if (!Dialogs.Confirm(this, "Clear counters", $"clear counters on {sel.Count} port(s)?")) return;
        foreach (var p in sel)
        {
            try { await Session!.RunInteractiveAsync($"clear counters {p.Name}", ""); }
            catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); return; }
        }
        Toast("Counters cleared", ControlAppearance.Success);
    }
}
