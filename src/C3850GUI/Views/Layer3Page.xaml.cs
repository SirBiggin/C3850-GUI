using System.Text.RegularExpressions;
using System.Windows;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

public class IpInterface
{
    public string Name = ""; public string Ip = ""; public string Method = ""; public string Status = ""; public string Protocol = ""; public string Helpers = "";
    public bool IsSvi => Name.StartsWith("Vlan", StringComparison.OrdinalIgnoreCase);
    public int VlanId => int.TryParse(Name[4..], out var v) ? v : 0;
    public string Display => Name;
}

public class DhcpPool
{
    public string Name = ""; public string Range = ""; public int Leased; public int Excluded; public int Total;
    public int Usable => Math.Max(1, Total - Excluded);
}

public partial class Layer3Page : SwitchPage
{
    private List<IpInterface> _ifs = new();
    private List<DhcpPool> _pools = new();
    private bool _routing;

    public Layer3Page() { InitializeComponent(); }

    private async void Refresh_Click(object s, RoutedEventArgs e) { if (RequireConnection()) await SafeRefreshAsync(); }

    protected override async Task RefreshAsync()
    {
        var s = Session!;
        // interfaces
        _ifs = ParseIpBrief((await s.RunAsync("show ip interface brief")).Output);
        var helpers = (await s.RunAsync("show running-config | include ^interface|ip helper-address")).Output;
        string cur = "";
        foreach (var l in helpers.Split('\n'))
        {
            var t = l.Trim();
            if (t.StartsWith("interface ")) cur = t[10..].Trim();
            else if (t.StartsWith("ip helper-address"))
            {
                var i = _ifs.FirstOrDefault(x => x.Name.Equals(cur, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(ShortName(cur), StringComparison.OrdinalIgnoreCase));
                if (i != null) i.Helpers = (i.Helpers + " " + t[17..].Trim()).Trim();
            }
        }
        IfGrid.ItemsSource = _ifs.Select(i => new { i.Name, i.Ip, i.Method, i.Status, i.Protocol, i.Helpers, Obj = i }).ToList();

        // routing
        var rt = (await s.RunAsync("show running-config | include ^ip routing|^no ip routing")).Output;
        _routing = rt.Contains("ip routing") && !rt.Contains("no ip routing");
        RoutingText.Text = _routing ? "ip routing: enabled" : "ip routing: disabled (L2 only)";
        RoutingPill.Background = new System.Windows.Media.SolidColorBrush(_routing ? System.Windows.Media.Color.FromArgb(0x44, 0x3F, 0xB9, 0x50) : System.Windows.Media.Color.FromArgb(0x55, 0xE0, 0xA0, 0x30));
        RoutingToggle.Content = _routing ? "Disable ip routing" : "Enable ip routing";
        Routes.Text = (await s.RunAsync("show ip route")).Output;

        // dhcp
        _pools = ParsePools((await s.RunAsync("show ip dhcp pool")).Output);
        PoolGrid.ItemsSource = _pools;
        var ex = (await s.RunAsync("show running-config | include ip dhcp excluded-address")).Output;
        ExcludedText.Text = ex.Trim().Length == 0 ? "No excluded addresses." : ex.Trim();
        Sub.Text = $"{_ifs.Count} IP interfaces  ·  {_pools.Count} DHCP pool{(_pools.Count == 1 ? "" : "s")}  ·  {_pools.Sum(p => p.Leased)} leases";
    }

    private static string ShortName(string n) => n.Replace("GigabitEthernet", "Gi").Replace("TenGigabitEthernet", "Te").Replace("Port-channel", "Po");

    private static List<IpInterface> ParseIpBrief(string output)
    {
        var list = new List<IpInterface>();
        foreach (var raw in output.Replace("\r", "").Split('\n'))
        {
            var m = Regex.Match(raw, @"^(\S+)\s+(\S+)\s+(YES|NO)\s+(\S+)\s+(.+?)\s+(up|down)\s*$", RegexOptions.IgnoreCase);
            if (m.Success) list.Add(new IpInterface { Name = m.Groups[1].Value, Ip = m.Groups[2].Value, Method = m.Groups[4].Value, Status = m.Groups[5].Value.Trim(), Protocol = m.Groups[6].Value });
        }
        return list;
    }

    private static List<DhcpPool> ParsePools(string output)
    {
        var list = new List<DhcpPool>(); DhcpPool? cur = null;
        foreach (var raw in output.Replace("\r", "").Split('\n'))
        {
            var l = raw.Trim();
            var pm = Regex.Match(l, @"^Pool (\S+) :");
            if (pm.Success) { cur = new DhcpPool { Name = pm.Groups[1].Value }; list.Add(cur); continue; }
            if (cur == null) continue;
            var tm = Regex.Match(l, @"^Total addresses\s*:\s*(\d+)"); if (tm.Success) cur.Total = int.Parse(tm.Groups[1].Value);
            var lm = Regex.Match(l, @"^Leased addresses\s*:\s*(\d+)"); if (lm.Success) cur.Leased = int.Parse(lm.Groups[1].Value);
            var em = Regex.Match(l, @"^Excluded addresses\s*:\s*(\d+)"); if (em.Success) cur.Excluded = int.Parse(em.Groups[1].Value);
            var rm = Regex.Match(l, @"^\S+\s+(\d+\.\d+\.\d+\.\d+)\s+-\s+(\d+\.\d+\.\d+\.\d+)"); if (rm.Success) cur.Range = $"{rm.Groups[1].Value} - {rm.Groups[2].Value}";
        }
        return list;
    }

    // ------------------------------------------------------------------ interfaces

    private IpInterface? SelectedIf()
    {
        var o = IfGrid.SelectedItem; if (o == null) { Toast("Select an interface first.", ControlAppearance.Caution); return null; }
        return (IpInterface)o.GetType().GetProperty("Obj")!.GetValue(o)!;
    }

    private async void NewSvi_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "New SVI", ("VLAN ID", "", "e.g. 20"), ("IP address", "", "e.g. 10.214.20.1"), ("Subnet mask", "255.255.255.0", ""), ("Description", "", "optional"), ("DHCP relay helper (optional)", "", "IP of your DHCP server, if not this switch"));
        if (f == null || !int.TryParse(f["VLAN ID"], out var id)) return;
        var lines = new List<string> { $"interface Vlan{id}" };
        if (f["Description"].Trim().Length > 0) lines.Add($"description {f["Description"].Trim()}");
        if (f["IP address"].Trim().Length > 0) lines.Add($"ip address {f["IP address"].Trim()} {f["Subnet mask"].Trim()}");
        if (f["DHCP relay helper (optional)"].Trim().Length > 0) lines.Add($"ip helper-address {f["DHCP relay helper (optional)"].Trim()}");
        lines.Add("no shutdown"); lines.Add("exit");
        if (await ConfigureAsync($"SVI Vlan{id}", lines.ToArray())) await SafeRefreshAsync();
    }

    private async void SetIp_Click(object s, RoutedEventArgs e)
    {
        var i = SelectedIf(); if (i == null) return;
        var f = Dialogs.Form(this, $"IP address on {i.Name}", ("IP address (blank = remove)", i.Ip == "unassigned" ? "" : i.Ip, ""), ("Subnet mask", "255.255.255.0", ""));
        if (f == null) return;
        var ip = f["IP address (blank = remove)"].Trim();
        var lines = new List<string> { $"interface {i.Name}" };
        if (!i.IsSvi && !i.Name.StartsWith("Loopback", StringComparison.OrdinalIgnoreCase)) lines.Add("no switchport"); // routed physical port
        lines.Add(ip.Length == 0 ? "no ip address" : $"ip address {ip} {f["Subnet mask"].Trim()}");
        lines.Add("exit");
        if (await ConfigureAsync($"IP on {i.Name}", lines.ToArray())) await SafeRefreshAsync();
    }

    private async void Helper_Click(object s, RoutedEventArgs e)
    {
        var i = SelectedIf(); if (i == null) return;
        var v = Dialogs.Prompt(this, $"DHCP relay on {i.Name}", "Helper address(es), space separated. Blank removes all. Clients on this SVI get their DHCP forwarded here.", i.Helpers);
        if (v == null) return;
        var lines = new List<string> { $"interface {i.Name}" };
        foreach (var h in i.Helpers.Split(' ', StringSplitOptions.RemoveEmptyEntries)) lines.Add($"no ip helper-address {h}");
        foreach (var h in v.Split(' ', StringSplitOptions.RemoveEmptyEntries)) lines.Add($"ip helper-address {h}");
        lines.Add("exit");
        if (await ConfigureAsync($"Helper on {i.Name}", lines.ToArray())) await SafeRefreshAsync();
    }

    private async void IfUp_Click(object s, RoutedEventArgs e) { var i = SelectedIf(); if (i != null && await ConfigureAsync($"no shut {i.Name}", $"interface {i.Name}", "no shutdown", "exit")) await SafeRefreshAsync(); }
    private async void IfDown_Click(object s, RoutedEventArgs e) { var i = SelectedIf(); if (i != null && await ConfigureAsync($"shut {i.Name}", $"interface {i.Name}", "shutdown", "exit")) await SafeRefreshAsync(); }

    private async void IfConfig_Click(object s, RoutedEventArgs e)
    {
        var i = SelectedIf(); if (i == null) return;
        var r = await RunAsync($"show running-config interface {i.Name}"); if (r != null) Dialogs.ShowText(this, i.Name, r.Output);
    }

    private async void DeleteSvi_Click(object s, RoutedEventArgs e)
    {
        var i = SelectedIf(); if (i == null) return;
        if (!i.IsSvi) { Toast("Only SVIs (interface VlanN) can be deleted here.", ControlAppearance.Caution); return; }
        if (!Dialogs.Confirm(this, "Delete SVI", $"no interface {i.Name}\n\nRemoves the L3 interface (the VLAN itself stays).", "Delete", true)) return;
        if (await ConfigureAsync($"Delete {i.Name}", $"no interface {i.Name}")) await SafeRefreshAsync();
    }

    // ------------------------------------------------------------------ routing

    private async void RoutingToggle_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection()) return;
        if (_routing && !Dialogs.Confirm(this, "Disable ip routing", "no ip routing\n\nThe switch stops routing between VLANs. SVIs keep their addresses but only the management path works.", "Disable", true)) return;
        if (await ConfigureAsync(_routing ? "Disable routing" : "Enable routing", _routing ? "no ip routing" : "ip routing")) await SafeRefreshAsync();
    }

    private async void AddRoute_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "Add static route", ("Destination network", "0.0.0.0", "0.0.0.0 for a default route"), ("Mask", "0.0.0.0", ""), ("Next hop (IP or interface)", "", "e.g. 10.214.0.1"), ("Admin distance (optional)", "", "1-255"), ("Name (optional)", "", ""));
        if (f == null || f["Next hop (IP or interface)"].Trim().Length == 0) return;
        var line = $"ip route {f["Destination network"].Trim()} {f["Mask"].Trim()} {f["Next hop (IP or interface)"].Trim()}";
        if (f["Admin distance (optional)"].Trim().Length > 0) line += " " + f["Admin distance (optional)"].Trim();
        if (f["Name (optional)"].Trim().Length > 0) line += " name " + f["Name (optional)"].Trim().Replace(' ', '_');
        if (await ConfigureAsync("Add route", line)) await SafeRefreshAsync();
    }

    private async void DelRoute_Click(object s, RoutedEventArgs e)
    {
        var r = await RunAsync("show running-config | include ^ip route"); if (r == null) return;
        var routes = r.Output.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("ip route")).ToList();
        if (routes.Count == 0) { Toast("No static routes configured.", ControlAppearance.Caution); return; }
        var v = Dialogs.Prompt(this, "Remove static route", "Configured routes:\n" + string.Join("\n", routes) + "\n\nPaste the route line to remove (exactly as above):", routes[0]);
        if (string.IsNullOrWhiteSpace(v) || !v.Trim().StartsWith("ip route")) return;
        if (await ConfigureAsync("Remove route", "no " + v.Trim())) await SafeRefreshAsync();
    }

    private async void FullRoute_Click(object s, RoutedEventArgs e) { var r = await RunAsync("show ip route"); if (r != null) Dialogs.ShowText(this, "show ip route", r.Output); }
    private async void Protocols_Click(object s, RoutedEventArgs e) { var r = await RunAsync("show ip protocols"); if (r != null) Dialogs.ShowText(this, "show ip protocols", r.Output); }

    // ------------------------------------------------------------------ dhcp

    private DhcpPool? SelectedPool()
    {
        if (PoolGrid.SelectedItem is DhcpPool p) return p;
        Toast("Select a pool first.", ControlAppearance.Caution); return null;
    }

    private async Task PoolDialogAsync(DhcpPool? existing)
    {
        var cur = new Dictionary<string, string>();
        if (existing != null)
        {
            var r = await RunAsync("show running-config | section ip dhcp pool " + existing.Name);
            if (r != null)
                foreach (var l in r.Output.Split('\n').Select(x => x.Trim()))
                {
                    var m = Regex.Match(l, @"^network (\S+) (\S+)"); if (m.Success) { cur["net"] = m.Groups[1].Value; cur["mask"] = m.Groups[2].Value; }
                    m = Regex.Match(l, @"^default-router (.+)$"); if (m.Success) cur["gw"] = m.Groups[1].Value;
                    m = Regex.Match(l, @"^dns-server (.+)$"); if (m.Success) cur["dns"] = m.Groups[1].Value;
                    m = Regex.Match(l, @"^domain-name (\S+)"); if (m.Success) cur["dom"] = m.Groups[1].Value;
                    m = Regex.Match(l, @"^lease (.+)$"); if (m.Success) cur["lease"] = m.Groups[1].Value;
                    m = Regex.Match(l, @"^option 150 ip (.+)$"); if (m.Success) cur["tftp"] = m.Groups[1].Value;
                }
        }
        string G(string k, string d = "") => cur.GetValueOrDefault(k, d);
        var f = Dialogs.Form(this, existing == null ? "New DHCP pool" : $"Edit pool {existing.Name}",
            ("Pool name", existing?.Name ?? "", "e.g. VLAN20"),
            ("Network", G("net"), "e.g. 10.214.20.0"),
            ("Mask", G("mask", "255.255.255.0"), ""),
            ("Default router (gateway)", G("gw"), "usually this switch's SVI address"),
            ("DNS servers (space separated)", G("dns"), "e.g. 10.214.20.3 1.1.1.1"),
            ("Domain name", G("dom"), "optional"),
            ("Lease (days [hours [minutes]] | infinite)", G("lease", "1"), "e.g. 7  or  0 12"),
            ("Option 150 TFTP (VoIP, optional)", G("tftp"), ""));
        if (f == null) return;
        var name = f["Pool name"].Trim(); if (name.Length == 0) return;
        var lines = new List<string> { $"ip dhcp pool {name}" };
        if (f["Network"].Trim().Length > 0) lines.Add($"network {f["Network"].Trim()} {f["Mask"].Trim()}");
        if (f["Default router (gateway)"].Trim().Length > 0) lines.Add($"default-router {f["Default router (gateway)"].Trim()}");
        if (f["DNS servers (space separated)"].Trim().Length > 0) lines.Add($"dns-server {f["DNS servers (space separated)"].Trim()}");
        if (f["Domain name"].Trim().Length > 0) lines.Add($"domain-name {f["Domain name"].Trim()}");
        if (f["Lease (days [hours [minutes]] | infinite)"].Trim().Length > 0) lines.Add($"lease {f["Lease (days [hours [minutes]] | infinite)"].Trim()}");
        if (f["Option 150 TFTP (VoIP, optional)"].Trim().Length > 0) lines.Add($"option 150 ip {f["Option 150 TFTP (VoIP, optional)"].Trim()}");
        lines.Add("exit");
        if (await ConfigureAsync($"DHCP pool {name}", lines.ToArray()))
        {
            if (existing == null) Toast("Tip: exclude the gateway and any static addresses with 'Exclude range…'.", ControlAppearance.Info, 6);
            await SafeRefreshAsync();
        }
    }

    private async void NewPool_Click(object s, RoutedEventArgs e) { if (RequireConnection()) await PoolDialogAsync(null); }
    private async void EditPool_Click(object s, RoutedEventArgs e) { var p = SelectedPool(); if (p != null) await PoolDialogAsync(p); }

    private async void DelPool_Click(object s, RoutedEventArgs e)
    {
        var p = SelectedPool(); if (p == null) return;
        if (!Dialogs.Confirm(this, "Delete DHCP pool", $"no ip dhcp pool {p.Name}\n\nExisting leases keep working until they expire; renewals will fail.", "Delete", true)) return;
        if (await ConfigureAsync($"Delete pool {p.Name}", $"no ip dhcp pool {p.Name}")) await SafeRefreshAsync();
    }

    private async void Exclude_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "Exclude addresses from DHCP", ("Start address", "", "e.g. 10.214.20.1"), ("End address (blank = single)", "", "e.g. 10.214.20.20"), ("Remove instead of add? (yes/no)", "no", ""));
        if (f == null || f["Start address"].Trim().Length == 0) return;
        var line = $"{(f["Remove instead of add? (yes/no)"].Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase) ? "no " : "")}ip dhcp excluded-address {f["Start address"].Trim()} {f["End address (blank = single)"].Trim()}".TrimEnd();
        if (await ConfigureAsync("Excluded addresses", line)) await SafeRefreshAsync();
    }

    private async void StaticBind_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "Static DHCP binding (host pool)",
            ("Pool name", "", "one pool per reservation, e.g. PRINTER1"),
            ("Host IP", "", "e.g. 10.214.20.50"), ("Mask", "255.255.255.0", ""),
            ("Client MAC (aabb.ccdd.eeff)", "", ""),
            ("Default router", "", ""), ("DNS servers", "", "optional"));
        if (f == null || f["Pool name"].Trim().Length == 0 || f["Host IP"].Trim().Length == 0) return;
        var mac = f["Client MAC (aabb.ccdd.eeff)"].Trim().ToLowerInvariant().Replace(":", "").Replace("-", "").Replace(".", "");
        if (mac.Length != 12) { Toast("MAC must be 12 hex digits.", ControlAppearance.Caution); return; }
        mac = $"{mac[..4]}.{mac[4..8]}.{mac[8..]}";
        // hardware-address matches on the MAC directly (client-identifier would need the 01 prefix form).
        var lines = new List<string> { $"ip dhcp pool {f["Pool name"].Trim()}", $"host {f["Host IP"].Trim()} {f["Mask"].Trim()}", $"hardware-address {mac}" };
        if (f["Default router"].Trim().Length > 0) lines.Add($"default-router {f["Default router"].Trim()}");
        if (f["DNS servers"].Trim().Length > 0) lines.Add($"dns-server {f["DNS servers"].Trim()}");
        lines.Add("exit");
        if (await ConfigureAsync("Static binding", lines.ToArray())) await SafeRefreshAsync();
    }

    private async void Bindings_Click(object s, RoutedEventArgs e) { var r = await RunAsync("show ip dhcp binding"); if (r != null) Dialogs.ShowText(this, "DHCP bindings", r.Output); }
    private async void Conflicts_Click(object s, RoutedEventArgs e) { var r = await RunAsync("show ip dhcp conflict"); if (r != null) Dialogs.ShowText(this, "DHCP conflicts", r.Output.Trim().Length == 0 ? "(none)" : r.Output); }

    private async void ClearBinding_Click(object s, RoutedEventArgs e)
    {
        var v = Dialogs.Prompt(this, "Clear DHCP binding", "IP address to release, or * for all:", "*");
        if (string.IsNullOrWhiteSpace(v) || !RequireConnection()) return;
        if (v.Trim() == "*" && !Dialogs.Confirm(this, "Clear all bindings", "clear ip dhcp binding *\n\nEvery client will need to renew.", "Clear all", true)) return;
        try { await Session!.RunInteractiveAsync($"clear ip dhcp binding {v.Trim()}", ""); await SafeRefreshAsync(); }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private async void DhcpConfig_Click(object s, RoutedEventArgs e)
    {
        var r = await RunAsync("show running-config | section dhcp"); if (r != null) Dialogs.ShowText(this, "DHCP configuration", r.Output);
    }
}
