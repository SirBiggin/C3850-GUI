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
        if (Session!.IsSerial)
        {
            // 'show interfaces switchport' is ~40 KB on a 48-port stack — far too slow at console speeds; derive mode from the status table instead.
            foreach (var p in _all)
                p.Mode = p.Vlan.Equals("trunk", StringComparison.OrdinalIgnoreCase) ? "trunk" : p.Vlan.Equals("routed", StringComparison.OrdinalIgnoreCase) ? "routed" : int.TryParse(p.Vlan, out _) ? "access" : "";
            foreach (var p in _all.Where(p => p.Mode == "access")) p.AccessVlan = p.Vlan;
        }
        else try
        {
            var sp = IosParser.InterfacesSwitchport((await Session!.RunAsync("show interfaces switchport", default, TimeSpan.FromSeconds(60))).Output);
            foreach (var p in _all)
                if (sp.TryGetValue(p.Name, out var i))
                {
                    p.Mode = IosParser.SimplifyMode(i); p.OperMode = i.OperMode; p.AccessVlan = i.AccessVlan;
                    p.NativeVlan = i.NativeVlan; p.AllowedVlans = i.Allowed; p.VoiceVlan = i.Voice;
                }
                else if (p.Vlan.Equals("routed", StringComparison.OrdinalIgnoreCase)) p.Mode = "routed";
        }
        catch (Exception ex) { Toast($"show interfaces switchport: {ex.Message}", ControlAppearance.Caution); }
        ApplyFilter();
        BuildPanel();
    }

    // ------------------------------------------------------------------ front panel

    private static readonly Regex PhysRe = new(@"^(?<pfx>[A-Za-z]+)(?<sw>\d+)/(?<slot>\d+)/(?<port>\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// Draws each stack member like its faceplate: slot-0 ports either stacked (odd top / even bottom,
    /// the C3850 arrangement) or in one row, grouped every N ports, with network-module ports (slot 1+) on the right.
    /// </summary>
    private void BuildPanel()
    {
        PanelHost.Children.Clear();
        var profile = Session?.Profile;
        var members = _all.Select(p => PhysRe.Match(p.Name)).Where(m => m.Success).Select(m => int.Parse(m.Groups["sw"].Value)).Distinct().OrderBy(x => x);
        foreach (var sw in members)
        {
            var layout = profile?.MemberLayouts.GetValueOrDefault(sw) ?? "stacked";
            var group = profile?.PortGroupSize ?? 12;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };

            var head = new StackPanel { Width = 118, VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(new System.Windows.Controls.TextBlock { Text = $"Switch {sw}", FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
            var pick = new ComboBox { FontSize = 11, Height = 26, MinHeight = 26, Padding = new Thickness(6, 0, 0, 0), Margin = new Thickness(0, 2, 8, 0), Tag = sw };
            pick.Items.Add(new ComboBoxItem { Content = "Stacked", Tag = "stacked", ToolTip = "Odd ports on top, even below (C3850 faceplate)" });
            pick.Items.Add(new ComboBoxItem { Content = "Single row", Tag = "row" });
            pick.SelectedIndex = layout == "row" ? 1 : 0;
            pick.SelectionChanged += Layout_Changed;
            head.Children.Add(pick);
            row.Children.Add(head);

            var ports = _all.Select(p => (p, m: PhysRe.Match(p.Name))).Where(x => x.m.Success && int.Parse(x.m.Groups["sw"].Value) == sw).ToList();
            foreach (var slot in ports.Select(x => int.Parse(x.m.Groups["slot"].Value)).Distinct().OrderBy(x => x))
            {
                var slotPorts = ports.Where(x => int.Parse(x.m.Groups["slot"].Value) == slot)
                                     .OrderBy(x => int.Parse(x.m.Groups["port"].Value)).Select(x => x.p).ToList();
                var isModule = slot > 0;
                row.Children.Add(PortBlock(slotPorts, isModule ? "row" : layout, isModule ? 0 : group, isModule));
            }
            PanelHost.Children.Add(row);
        }
    }

    private System.Windows.Controls.Grid PortBlock(List<PortInfo> ports, string layout, int groupSize, bool isModule)
    {
        var g = new System.Windows.Controls.Grid { Margin = new Thickness(isModule ? 18 : 0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bool stacked = layout == "stacked";
        int rows = stacked ? 2 : 1;
        for (int r = 0; r < rows; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        int perGroupCols = stacked ? Math.Max(1, groupSize / 2) : groupSize;
        var cells = new List<(PortInfo p, int r, int c)>();
        int cols = 0;
        for (int i = 0; i < ports.Count; i++)
        {
            int r = stacked ? i % 2 : 0;
            int c = stacked ? i / 2 : i;
            if (groupSize > 0) c += c / perGroupCols;          // one spacer column after every group
            cells.Add((ports[i], r, c));
            cols = Math.Max(cols, c + 1);
        }
        for (int c = 0; c < cols; c++)
        {
            bool spacer = groupSize > 0 && (c + 1) % (perGroupCols + 1) == 0;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = spacer ? new GridLength(10) : GridLength.Auto });
        }
        foreach (var (p, r, c) in cells)
        {
            var b = new Border
            {
                Width = isModule ? 26 : 22, Height = 15, Margin = new Thickness(1.5), CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand,
                Background = (System.Windows.Media.Brush)new Converters.PortStatusBrushConverter().Convert(p.Status, typeof(System.Windows.Media.Brush), null!, null!),
                Tag = p, ToolTip = $"{p.Name}\n{p.Description}\n{p.Status}  {p.Speed}  {p.Type}\n{p.Mode} {p.ModeDetail}".Trim(),
                BorderBrush = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(p.IsSelected ? 2 : 0)
            };
            b.Child = new System.Windows.Controls.TextBlock { Text = Regex.Match(p.Name, @"(\d+)$").Groups[1].Value, FontSize = 8.5, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };
            b.MouseLeftButtonDown += PortBox_Click;
            b.MouseRightButtonDown += PortBox_RightClick;
            p.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PortInfo.IsSelected)) b.BorderThickness = new Thickness(p.IsSelected ? 2 : 0); };
            System.Windows.Controls.Grid.SetRow(b, r); System.Windows.Controls.Grid.SetColumn(b, c);
            g.Children.Add(b);
        }
        return g;
    }

    private void Layout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb || cb.Tag is not int sw || Session == null) return;
        Session.Profile.MemberLayouts[sw] = ((ComboBoxItem)cb.SelectedItem).Tag?.ToString() ?? "stacked";
        App.Store.Save();
        BuildPanel();
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
            if (f.Length == 0 || $"{p.Name} {p.Description} {p.Status} {p.Vlan} {p.Type} {p.Mode} {p.ModeDetail}".Contains(f, StringComparison.OrdinalIgnoreCase))
                _view.Add(p);
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (PortInfo p in e.RemovedItems) p.IsSelected = false;
        foreach (PortInfo p in e.AddedItems) p.IsSelected = true;
    }

    /// <summary>Right-click on a row that isn't selected selects just that row, so the menu acts on what you pointed at.</summary>
    private void Grid_RightClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? d = e.OriginalSource as DependencyObject;
        while (d != null && d is not DataGridRow) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        if (d is DataGridRow { Item: PortInfo p } && !Grid.SelectedItems.Contains(p)) { Grid.SelectedItems.Clear(); Grid.SelectedItems.Add(p); }
    }

    private void PortBox_RightClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PortInfo p) return;
        if (!Grid.SelectedItems.Contains(p)) { Grid.SelectedItems.Clear(); Grid.SelectedItems.Add(p); }
        var menu = (ContextMenu)FindResource("PortMenu");
        menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
        e.Handled = true;
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
        var vlans = await LoadVlansAsync(); if (vlans == null) return;
        int.TryParse(sel[0].AccessVlan.Length > 0 ? sel[0].AccessVlan : sel[0].Vlan, out var curAccess);
        int.TryParse(sel[0].VoiceVlan, out var curVoice);
        var r = TrunkDialog.ShowAccess(this, vlans, curAccess, curVoice, sel.Count);
        if (r == null) return;
        var lines = new List<string> { "switchport mode access", $"switchport access vlan {r.Value.access}" };
        lines.Add(r.Value.voice > 0 ? $"switchport voice vlan {r.Value.voice}" : "no switchport voice vlan");
        await ApplyAsync("Access VLAN", lines.ToArray());
    }

    /// <summary>"interface X" or "interface range X" depending on whether the spec is a list/range.</summary>
    private static string InterfaceCmd(string spec) => (spec.Contains(',') || spec.Contains('-') ? "interface range " : "interface ") + spec.Trim();

    private string SelectionSpec() => string.Join(",", Selected.Select(p => p.Name));

    private async Task<List<VlanInfo>?> LoadVlansAsync()
    {
        var r = await RunAsync("show vlan brief"); if (r == null) return null;
        var v = IosParser.VlanBrief(r.Output);
        if (v.Count == 0) { Toast("Couldn't read the VLAN list from the switch.", ControlAppearance.Caution); return null; }
        return v;
    }

    /// <summary>Current allowed/native for a port, from the switchport data loaded with the table.</summary>
    private static (string allowed, int native) CurrentTrunk(PortInfo p)
    {
        var allowed = string.IsNullOrEmpty(p.AllowedVlans) ? "all" : p.AllowedVlans;
        var native = int.TryParse(p.NativeVlan, out var n) ? n : 1;
        return (allowed, native);
    }

    private async void Trunk_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection()) return;
        var vlans = await LoadVlansAsync(); if (vlans == null) return;
        var sel = Selected;
        var (curAllowed, curNative) = sel.Count == 1 ? CurrentTrunk(sel[0]) : ("all", 1);
        var d = TrunkDialog.Show(this, "Trunk / VLANs", vlans, SelectionSpec(), false, curAllowed, curNative);
        if (d == null) return;
        var lines = new List<string> { InterfaceCmd(d.Interfaces) };
        if (d.Description.Length > 0) lines.Add($"description {d.Description}");
        lines.AddRange(new[] { "switchport mode trunk", $"switchport trunk native vlan {d.Native}", $"switchport trunk allowed vlan {d.Allowed}", "exit" });
        if (await ConfigureAsync($"Trunk {d.Interfaces}", lines.ToArray())) await SafeRefreshAsync();
    }

    public static bool VlanListContains(string list, int vlan)
    {
        foreach (var part in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var r = part.Split('-');
            if (r.Length == 2 && int.TryParse(r[0], out var a) && int.TryParse(r[1], out var b) && vlan >= a && vlan <= b) return true;
            if (r.Length == 1 && int.TryParse(r[0], out var v) && v == vlan) return true;
        }
        return false;
    }

    private async void PortChannel_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection()) return;
        var vlans = await LoadVlansAsync(); if (vlans == null) return;
        var d = TrunkDialog.Show(this, "Port-channel (EtherChannel trunk)", vlans, SelectionSpec(), true);
        if (d == null) return;

        // Members must match the Po's switchport config or they won't bundle, so both get the same lines.
        string[] trunk = { "switchport mode trunk", $"switchport trunk native vlan {d.Native}", $"switchport trunk allowed vlan {d.Allowed}" };
        var lines = new List<string> { InterfaceCmd(d.Interfaces) };
        lines.AddRange(trunk);
        lines.Add($"channel-group {d.Channel} mode {d.Mode}");
        lines.Add("no shutdown");
        lines.Add("exit");
        lines.Add($"interface Port-channel{d.Channel}");
        if (d.Description.Length > 0) lines.Add($"description {d.Description}");
        lines.AddRange(trunk);
        lines.Add("no shutdown");
        lines.Add("exit");
        if (await ConfigureAsync($"Port-channel{d.Channel} ({d.Mode})", lines.ToArray()))
        {
            var r = await RunAsync($"show etherchannel {d.Channel} summary");
            if (r != null) Dialogs.ShowText(this, $"Port-channel{d.Channel}", r.Output);
            await SafeRefreshAsync();
        }
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

    private async void Default_Click(object s, RoutedEventArgs e)
    {
        var sel = NeedSelection(); if (sel == null) return;
        var names = string.Join(", ", sel.Select(p => p.ShortName));
        if (!Dialogs.Confirm(this, "Default interface(s)",
            $"default interface {(sel.Count > 1 ? "range " : "")}{names}\n\nThis removes ALL configuration from the port(s): description, VLANs, trunking, PoE, port-security, spanning-tree settings, channel-group, etc.\nThe port stays administratively up unless it was default-shut.", "Reset to default", true)) return;
        // 'default interface' takes a range directly in global config; do it in chunks of 8 to keep lines short.
        var lines = new List<string>();
        foreach (var chunk in sel.Chunk(8)) lines.Add($"default interface {(chunk.Length > 1 ? "range " : "")}{string.Join(",", chunk.Select(p => p.Name))}");
        if (!RequireConnection()) return;
        try
        {
            var r = await Session!.ConfigureAsync(lines);
            if (r.Error) Toast(r.ErrorText, ControlAppearance.Danger, 8);
            else Toast($"Defaulted {sel.Count} port{(sel.Count == 1 ? "" : "s")}", ControlAppearance.Success);
            await SafeRefreshAsync();
        }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
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
