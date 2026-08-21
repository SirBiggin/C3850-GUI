using System.Text.RegularExpressions;
using System.Windows;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

public partial class SystemPage : SwitchPage
{
    private string _cfg = "";           // the running-config lines we care about
    private bool _http, _https, _telnet, _ssh, _cdp, _lldp;
    private List<string> _vtyTransport = new();

    public SystemPage() { InitializeComponent(); }

    private async void Refresh_Click(object s, RoutedEventArgs e) { if (RequireConnection()) await SafeRefreshAsync(); }

    private IEnumerable<string> Lines(string prefix) => _cfg.Split('\n').Select(l => l.TrimEnd()).Where(l => l.StartsWith(prefix));
    private string First(string pattern, string dflt = "") { var m = Regex.Match(_cfg, pattern, RegexOptions.Multiline); return m.Success ? m.Groups[1].Value.Trim() : dflt; }

    protected override async Task RefreshAsync()
    {
        var s = Session!;
        // Only fetch the lines this page uses — filtered on the switch, so serial links don't carry the whole config.
        const string globals = "^hostname |^ip domain|^ip name-server|^ip default-gateway|^ip http|^ip ssh|^banner motd|^username |^aaa |^enable secret|^clock |^ntp |^logging |^snmp-server |^cdp run|^no cdp run|^lldp run|^errdisable |^spanning-tree |^vtp |^crypto pki|^ip routing|^no ip routing";
        var g = (await s.RunAsync($"show running-config | include {globals}", default, TimeSpan.FromSeconds(60))).Output;
        var vty = (await s.RunAsync("show running-config | section ^line vty", default, TimeSpan.FromSeconds(60))).Output;
        _cfg = (g + "\n" + vty).Replace("\r", "");

        // --- management
        _http = !Regex.IsMatch(_cfg, @"^no ip http server", RegexOptions.Multiline) && Regex.IsMatch(_cfg, @"^ip http server", RegexOptions.Multiline);
        _https = !Regex.IsMatch(_cfg, @"^no ip http secure-server", RegexOptions.Multiline) && Regex.IsMatch(_cfg, @"^ip http secure-server", RegexOptions.Multiline);
        HttpToggle.IsChecked = _http; HttpsToggle.IsChecked = _https;
        HttpState.Text = _http ? "enabled" : "disabled";
        HttpsState.Text = _https ? "enabled" : "disabled";

        // vty transport: look inside "line vty" blocks
        _vtyTransport = new();
        var vtyBlocks = Regex.Matches(_cfg, @"^line vty (\d+)(?: (\d+))?\n((?: .*\n?)*)", RegexOptions.Multiline);
        bool anyTelnet = false, anySsh = false, sawTransport = false;
        var info = new List<string>();
        foreach (Match b in vtyBlocks)
        {
            var body = b.Groups[3].Value;
            var t = Regex.Match(body, @"^ transport input (.+)$", RegexOptions.Multiline);
            var tr = t.Success ? t.Groups[1].Value.Trim() : "(default: all)";
            var to = Regex.Match(body, @"^ exec-timeout (\d+) (\d+)", RegexOptions.Multiline);
            var ac = Regex.Match(body, @"^ access-class (\S+) in", RegexOptions.Multiline);
            var login = Regex.Match(body, @"^ (login.*|no login)$", RegexOptions.Multiline);
            info.Add($"vty {b.Groups[1].Value}{(b.Groups[2].Success ? "-" + b.Groups[2].Value : "")}: transport {tr}{(to.Success ? $", exec-timeout {to.Groups[1].Value}:{to.Groups[2].Value}" : "")}{(ac.Success ? $", access-class {ac.Groups[1].Value}" : "")}{(login.Success ? ", " + login.Groups[1].Value : "")}");
            if (t.Success)
            {
                sawTransport = true;
                var v = t.Groups[1].Value.ToLowerInvariant();
                if (v.Contains("telnet") || v.Contains("all")) anyTelnet = true;
                if (v.Contains("ssh") || v.Contains("all")) anySsh = true;
            }
        }
        if (!sawTransport) { anyTelnet = true; anySsh = true; } // IOS default when nothing is configured
        _telnet = anyTelnet; _ssh = anySsh;
        TelnetToggle.IsChecked = _telnet; SshToggle.IsChecked = _ssh;
        TelnetState.Text = _telnet ? "allowed" : "blocked";
        SshState.Text = _ssh ? "allowed" : "blocked  (careful: this app uses SSH)";
        var sshVer = First(@"^ip ssh version (\d)", "(not set)");
        VtyInfo.Text = string.Join("\n", info) + $"\nip ssh version {sshVer}";

        // --- identity
        IdentityText.Text = $"hostname {First(@"^hostname (.+)$", "?")}\nip domain-name {First(@"^ip domain[- ]name (.+)$", "(none)")}\n" +
            $"name-servers: {string.Join(" ", Lines("ip name-server").Select(l => l[15..].Trim()))}{(Lines("ip name-server").Any() ? "" : "(none)")}\n" +
            $"ip default-gateway {First(@"^ip default-gateway (.+)$", "(none)")}\n" +
            (Regex.IsMatch(_cfg, @"^banner motd", RegexOptions.Multiline) ? "banner motd: set" : "banner motd: none");

        // --- users
        Users.ItemsSource = Lines("username ").Select(l => Regex.Replace(l, @"(secret|password) \d? ?\S+", "$1 ********")).ToList();
        var aaa = Lines("aaa ").ToList();
        AaaText.Text = aaa.Count == 0 ? "aaa new-model: off (line passwords / local users)" : string.Join("\n", aaa.Take(6)) + (aaa.Count > 6 ? $"\n… +{aaa.Count - 6} more" : "");

        // --- time
        var clock = (await s.RunAsync("show clock")).Output.Trim();
        ClockText.Text = $"{clock}\n{First(@"^(clock timezone .+)$", "clock timezone: UTC (default)")}\n{First(@"^(clock summer-time .+)$", "no summer-time")}\n" +
            (Lines("ntp server").Any() ? string.Join("\n", Lines("ntp server")) : "ntp: no servers");

        // --- logging
        var lg = Lines("logging ").ToList();
        LoggingText.Text = lg.Count == 0 ? "logging: defaults" : string.Join("\n", lg);

        // --- snmp
        var sn = Lines("snmp-server ").Select(l => Regex.Replace(l, @"community \S+", "community ****")).ToList();
        SnmpText.Text = sn.Count == 0 ? "snmp: not configured" : string.Join("\n", sn);

        // --- discovery
        _cdp = !Regex.IsMatch(_cfg, @"^no cdp run", RegexOptions.Multiline);
        _lldp = Regex.IsMatch(_cfg, @"^lldp run", RegexOptions.Multiline);
        CdpToggle.IsChecked = _cdp; LldpToggle.IsChecked = _lldp;
    }

    private async Task Apply(string what, params string[] lines) { if (await ConfigureAsync(what, lines)) await SafeRefreshAsync(); else await SafeRefreshAsync(); }

    // ------------------------------------------------------------------ management access

    private async void Http_Click(object s, RoutedEventArgs e) => await Apply(HttpToggle.IsChecked == true ? "Enable HTTP server" : "Disable HTTP server", HttpToggle.IsChecked == true ? "ip http server" : "no ip http server");

    private async void Https_Click(object s, RoutedEventArgs e)
    {
        var on = HttpsToggle.IsChecked == true;
        if (on && !Regex.IsMatch(_cfg, @"^crypto pki", RegexOptions.Multiline))
            Toast("HTTPS needs an RSA key / self-signed cert; IOS generates one automatically on first enable (may take a few seconds).", ControlAppearance.Info, 7);
        await Apply(on ? "Enable HTTPS server" : "Disable HTTPS server", on ? "ip http secure-server" : "no ip http secure-server");
    }

    private string TransportLine(bool telnet, bool ssh) => (telnet, ssh) switch { (true, true) => "transport input telnet ssh", (true, false) => "transport input telnet", (false, true) => "transport input ssh", _ => "transport input none" };

    private async void Telnet_Click(object s, RoutedEventArgs e)
    {
        var telnet = TelnetToggle.IsChecked == true;
        await Apply(telnet ? "Allow telnet" : "Block telnet", "line vty 0 15", TransportLine(telnet, _ssh), "exit");
    }

    private async void Ssh_Click(object s, RoutedEventArgs e)
    {
        var ssh = SshToggle.IsChecked == true;
        if (!ssh && !Dialogs.Confirm(this, "Block SSH", "transport input " + (_telnet ? "telnet" : "none") + "\n\nThis app connects over SSH. You will lose access unless telnet or console is available.", "Block SSH anyway", true))
        { SshToggle.IsChecked = true; return; }
        await Apply(ssh ? "Allow SSH" : "Block SSH", "line vty 0 15", TransportLine(_telnet, ssh), "exit");
    }

    private async void SshKeys_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection()) return;
        var f = Dialogs.Form(this, "SSH keys / version", ("RSA modulus (bits)", "2048", "2048 or 4096; blank = don't regenerate"), ("SSH version", "2", "2 recommended"), ("Auth retries", "3", ""), ("Timeout (s)", "60", ""));
        if (f == null) return;
        try
        {
            if (int.TryParse(f["RSA modulus (bits)"], out var bits))
            {
                if (!Dialogs.Confirm(this, "Regenerate RSA key", $"crypto key generate rsa modulus {bits}\n\nExisting SSH host key is replaced; clients will see a changed host key warning. This session stays up.", "Generate", true)) return;
                await Session!.RunSequenceAsync(new[] { "configure terminal" });
                var r = await Session.RunInteractiveAsync($"crypto key generate rsa modulus {bits}", "yes", default, TimeSpan.FromSeconds(120));
                await Session.RunSequenceAsync(new[] { "end" });
                if (r.Error) { Toast(r.ErrorText, ControlAppearance.Danger, 8); return; }
            }
            await Apply("SSH settings", $"ip ssh version {f["SSH version"].Trim()}", $"ip ssh authentication-retries {f["Auth retries"].Trim()}", $"ip ssh time-out {f["Timeout (s)"].Trim()}");
        }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private async void ExecTimeout_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "Exec timeout", ("Minutes", "10", "0 = never"), ("Seconds", "0", ""), ("Apply to", "vty 0 15", "vty 0 15 | console 0 | both"));
        if (f == null) return;
        var lines = new List<string>();
        var where = f["Apply to"].Trim().ToLowerInvariant();
        if (where.StartsWith("vty") || where == "both") lines.AddRange(new[] { "line vty 0 15", $"exec-timeout {f["Minutes"].Trim()} {f["Seconds"].Trim()}", "exit" });
        if (where.StartsWith("con") || where == "both") lines.AddRange(new[] { "line console 0", $"exec-timeout {f["Minutes"].Trim()} {f["Seconds"].Trim()}", "exit" });
        await Apply("Exec timeout", lines.ToArray());
    }

    private async void AccessClass_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "vty access-class", ("ACL name or number (blank = remove)", "MGMT", ""), ("Permitted source networks (space separated, blank = keep existing ACL)", "", "e.g. 10.214.0.0/16 10.214.20.0/24"));
        if (f == null) return;
        var acl = f["ACL name or number (blank = remove)"].Trim();
        var lines = new List<string>();
        if (acl.Length == 0) { lines.AddRange(new[] { "line vty 0 15", "no access-class", "exit" }); await Apply("Remove access-class", lines.ToArray()); return; }
        var nets = f["Permitted source networks (space separated, blank = keep existing ACL)"].Trim();
        if (nets.Length > 0)
        {
            lines.Add($"no ip access-list standard {acl}");
            lines.Add($"ip access-list standard {acl}");
            foreach (var n in nets.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = n.Split('/');
                if (parts.Length == 2 && int.TryParse(parts[1], out var cidr)) lines.Add($"permit {parts[0]} {Wildcard(cidr)}");
                else lines.Add($"permit host {parts[0]}");
            }
            lines.Add("exit");
        }
        lines.AddRange(new[] { "line vty 0 15", $"access-class {acl} in", "exit" });
        if (!Dialogs.Confirm(this, "Restrict management access", string.Join("\n", lines) + "\n\nMake sure your own address is permitted or you'll lock yourself out.", "Apply", true)) return;
        await Apply("vty access-class", lines.ToArray());
    }

    private static string Wildcard(int cidr) { uint m = cidr == 0 ? 0 : 0xFFFFFFFFu << (32 - cidr); uint w = ~m; return $"{(w >> 24) & 255}.{(w >> 16) & 255}.{(w >> 8) & 255}.{w & 255}"; }

    private async void Who_Click(object s, RoutedEventArgs e) { var r = await RunAsync("show users"); if (r != null) Dialogs.ShowText(this, "show users", r.Output); }

    // ------------------------------------------------------------------ identity

    private async void Hostname_Click(object s, RoutedEventArgs e)
    {
        var v = Dialogs.Prompt(this, "Hostname", "New hostname (no spaces):", Session?.Hostname ?? "");
        if (string.IsNullOrWhiteSpace(v)) return;
        await Apply("Hostname", $"hostname {v.Trim()}");
        Toast("Hostname changed — reconnect to refresh the prompt detection if anything looks off.", ControlAppearance.Info, 6);
    }

    private async void Domain_Click(object s, RoutedEventArgs e)
    {
        var v = Dialogs.Prompt(this, "Domain name", "ip domain-name (needed for SSH keys):", First(@"^ip domain[- ]name (.+)$"));
        if (string.IsNullOrWhiteSpace(v)) return;
        await Apply("Domain name", $"ip domain-name {v.Trim()}");
    }

    private async void Banner_Click(object s, RoutedEventArgs e)
    {
        var v = Dialogs.Prompt(this, "Banner MOTD", "Text shown before login (blank removes). Use | for line breaks; must not contain '^'.", "");
        if (v == null) return;
        if (v.Trim().Length == 0) { await Apply("Remove banner", "no banner motd"); return; }
        // one-line form with ^ delimiter; newlines inside a single config line don't survive our line sender
        await Apply("Banner", $"banner motd ^{v.Replace("|", "\n").Replace("\n", " ")}^");
    }

    private async void Dns_Click(object s, RoutedEventArgs e)
    {
        var cur = string.Join(" ", Lines("ip name-server").Select(l => l[15..].Trim()));
        var v = Dialogs.Prompt(this, "DNS servers", "Space separated (blank = none; also disables domain lookup):", cur);
        if (v == null) return;
        var lines = new List<string>();
        foreach (var l in Lines("ip name-server")) lines.Add("no " + l);
        if (v.Trim().Length == 0) lines.Add("no ip domain-lookup");
        else { lines.Add("ip domain-lookup"); lines.Add($"ip name-server {v.Trim()}"); }
        await Apply("DNS", lines.ToArray());
    }

    private async void Gateway_Click(object s, RoutedEventArgs e)
    {
        var v = Dialogs.Prompt(this, "Default gateway", "ip default-gateway (only used when ip routing is OFF; with routing on, use a static default route on the Layer 3 page):", First(@"^ip default-gateway (.+)$"));
        if (v == null) return;
        await Apply("Default gateway", v.Trim().Length == 0 ? "no ip default-gateway" : $"ip default-gateway {v.Trim()}");
    }

    // ------------------------------------------------------------------ users

    private async void AddUser_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "Local user", ("Username", "", ""), ("Password", "", "stored as a secret hash"), ("Privilege (1-15)", "15", ""));
        if (f == null || f["Username"].Trim().Length == 0 || f["Password"].Length == 0) return;
        await Apply($"User {f["Username"].Trim()}", $"username {f["Username"].Trim()} privilege {f["Privilege (1-15)"].Trim()} secret {f["Password"]}");
    }

    private async void DelUser_Click(object s, RoutedEventArgs e)
    {
        if (Users.SelectedItem is not string line) { Toast("Select a user first.", ControlAppearance.Caution); return; }
        var name = line.Split(' ')[1];
        if (name.Equals(Session?.Profile.Username, StringComparison.OrdinalIgnoreCase) && !Dialogs.Confirm(this, "Remove your own user", $"You're logged in as {name}. Removing it will lock you out at next login.", "Remove anyway", true)) return;
        if (!Dialogs.Confirm(this, "Remove user", $"no username {name}", "Remove", true)) return;
        try { await Session!.RunSequenceAsync(new[] { "configure terminal" }); await Session.RunInteractiveAsync($"no username {name}", "", default, TimeSpan.FromSeconds(15)); await Session.RunSequenceAsync(new[] { "end" }); await SafeRefreshAsync(); }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private async void EnableSecret_Click(object s, RoutedEventArgs e)
    {
        var v = Dialogs.Prompt(this, "Enable secret", "New enable secret (update the profile afterwards if this is what the app uses to enable):", "");
        if (string.IsNullOrEmpty(v)) return;
        await Apply("Enable secret", $"enable secret {v}");
    }

    // ------------------------------------------------------------------ time

    private async void Ntp_Click(object s, RoutedEventArgs e)
    {
        var cur = string.Join(" ", Lines("ntp server").Select(l => l.Split(' ')[2]));
        var v = Dialogs.Prompt(this, "NTP servers", "Space separated (first one becomes 'prefer'; blank = remove all):", cur);
        if (v == null) return;
        var lines = Lines("ntp server").Select(l => "no " + l).ToList();
        var srv = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < srv.Length; i++) lines.Add($"ntp server {srv[i]}{(i == 0 ? " prefer" : "")}");
        await Apply("NTP", lines.ToArray());
    }

    private async void Timezone_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "Timezone / DST", ("Zone name", "EST", "e.g. EST, CST, MST, PST, UTC"), ("UTC offset hours", "-5", ""), ("DST zone name (blank = no DST)", "EDT", ""), ("DST rule", "recurring", "recurring = US rules"));
        if (f == null) return;
        var lines = new List<string> { $"clock timezone {f["Zone name"].Trim()} {f["UTC offset hours"].Trim()}" };
        lines.Add(f["DST zone name (blank = no DST)"].Trim().Length == 0 ? "no clock summer-time" : $"clock summer-time {f["DST zone name (blank = no DST)"].Trim()} {f["DST rule"].Trim()}");
        await Apply("Timezone", lines.ToArray());
    }

    private async void SetClock_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection()) return;
        var now = DateTime.Now;
        var v = Dialogs.Prompt(this, "Set clock", "clock set HH:MM:SS DD MON YYYY (pre-filled with this PC's local time):", now.ToString("HH:mm:ss d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
        if (string.IsNullOrWhiteSpace(v)) return;
        try { var r = await Session!.RunAsync($"clock set {v.Trim()}"); Toast(r.Error ? r.ErrorText : "Clock set", r.Error ? ControlAppearance.Danger : ControlAppearance.Success); await SafeRefreshAsync(); }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private async void NtpStatus_Click(object s, RoutedEventArgs e)
    {
        var r = await RunAsync("show ntp status"); if (r == null) return;
        var r2 = await RunAsync("show ntp associations");
        Dialogs.ShowText(this, "NTP", r.Output + "\n\n" + (r2?.Output ?? ""));
    }

    // ------------------------------------------------------------------ logging

    private async void LogHosts_Click(object s, RoutedEventArgs e)
    {
        var cur = string.Join(" ", Lines("logging host").Select(l => l.Split(' ')[2]).Concat(Lines("logging ").Where(l => Regex.IsMatch(l, @"^logging \d+\.\d+\.\d+\.\d+$")).Select(l => l.Split(' ')[1])));
        var v = Dialogs.Prompt(this, "Syslog hosts", "Space separated (blank = remove all):", cur);
        if (v == null) return;
        var lines = new List<string>();
        foreach (var h in cur.Split(' ', StringSplitOptions.RemoveEmptyEntries)) lines.Add($"no logging host {h}");
        foreach (var h in v.Split(' ', StringSplitOptions.RemoveEmptyEntries)) lines.Add($"logging host {h}");
        await Apply("Syslog hosts", lines.ToArray());
    }

    private async void LogLevels_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "Logging levels", ("Buffer size (bytes)", First(@"^logging buffered (\d+)", "16384"), ""), ("Buffer level", "informational", "emergencies … debugging"), ("Trap (syslog) level", First(@"^logging trap (\S+)", "informational"), ""), ("Console level", "warnings", "blank = leave"));
        if (f == null) return;
        var lines = new List<string> { $"logging buffered {f["Buffer size (bytes)"].Trim()} {f["Buffer level"].Trim()}", $"logging trap {f["Trap (syslog) level"].Trim()}" };
        if (f["Console level"].Trim().Length > 0) lines.Add($"logging console {f["Console level"].Trim()}");
        await Apply("Logging levels", lines.ToArray());
    }

    private async void LogTimestamps_Click(object s, RoutedEventArgs e)
    {
        if (!Dialogs.Confirm(this, "Log timestamps", "service timestamps log datetime msec localtime show-timezone\nservice timestamps debug datetime msec localtime show-timezone\n\nMakes log lines carry local wall-clock time.")) return;
        await Apply("Timestamps", "service timestamps log datetime msec localtime show-timezone", "service timestamps debug datetime msec localtime show-timezone");
    }

    // ------------------------------------------------------------------ snmp

    private async void SnmpAdd_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "SNMP community", ("Community string", "", ""), ("Access", "RO", "RO | RW"), ("Restrict to ACL (optional)", "", "standard ACL name/number"));
        if (f == null || f["Community string"].Trim().Length == 0) return;
        var line = $"snmp-server community {f["Community string"].Trim()} {f["Access"].Trim().ToUpperInvariant()}";
        if (f["Restrict to ACL (optional)"].Trim().Length > 0) line += " " + f["Restrict to ACL (optional)"].Trim();
        await Apply("SNMP community", line);
    }

    private async void SnmpDel_Click(object s, RoutedEventArgs e)
    {
        var v = Dialogs.Prompt(this, "Remove SNMP community", "Community string to remove:", "");
        if (string.IsNullOrWhiteSpace(v)) return;
        await Apply("Remove community", $"no snmp-server community {v.Trim()}");
    }

    private async void SnmpInfo_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "SNMP location / contact", ("Location", First(@"^snmp-server location (.+)$"), ""), ("Contact", First(@"^snmp-server contact (.+)$"), ""));
        if (f == null) return;
        await Apply("SNMP info", $"snmp-server location {f["Location"].Trim()}", $"snmp-server contact {f["Contact"].Trim()}");
    }

    private async void SnmpTrap_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "SNMP trap host", ("Host", "", ""), ("Community", "", ""), ("Version", "2c", "1 | 2c"));
        if (f == null || f["Host"].Trim().Length == 0) return;
        await Apply("Trap host", $"snmp-server host {f["Host"].Trim()} version {f["Version"].Trim()} {f["Community"].Trim()}", "snmp-server enable traps");
    }

    // ------------------------------------------------------------------ discovery / other

    private async void Cdp_Click(object s, RoutedEventArgs e) => await Apply(CdpToggle.IsChecked == true ? "Enable CDP" : "Disable CDP", CdpToggle.IsChecked == true ? "cdp run" : "no cdp run");
    private async void Lldp_Click(object s, RoutedEventArgs e) => await Apply(LldpToggle.IsChecked == true ? "Enable LLDP" : "Disable LLDP", LldpToggle.IsChecked == true ? "lldp run" : "no lldp run");

    private async void ErrDisable_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "Err-disable recovery", ("Causes (space separated)", "all", "all | bpduguard psecure-violation link-flap udld …"), ("Interval (s)", First(@"^errdisable recovery interval (\d+)", "300"), ""));
        if (f == null) return;
        var lines = f["Causes (space separated)"].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(c => $"errdisable recovery cause {c}").ToList();
        lines.Add($"errdisable recovery interval {f["Interval (s)"].Trim()}");
        await Apply("Err-disable recovery", lines.ToArray());
    }

    private async void Stp_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "Spanning tree", ("Mode", First(@"^spanning-tree mode (\S+)", "rapid-pvst"), "pvst | rapid-pvst | mst"), ("Priority for all VLANs (blank = leave)", "", "0-61440 step 4096; 4096 = make this the root"), ("Portfast default on access ports (yes/no)", "yes", ""));
        if (f == null) return;
        var lines = new List<string> { $"spanning-tree mode {f["Mode"].Trim()}" };
        if (f["Priority for all VLANs (blank = leave)"].Trim().Length > 0) lines.Add($"spanning-tree vlan 1-4094 priority {f["Priority for all VLANs (blank = leave)"].Trim()}");
        if (f["Portfast default on access ports (yes/no)"].Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase)) { lines.Add("spanning-tree portfast default"); lines.Add("spanning-tree portfast bpduguard default"); }
        await Apply("Spanning tree", lines.ToArray());
    }

    private async void Vtp_Click(object s, RoutedEventArgs e)
    {
        var v = Dialogs.Prompt(this, "VTP mode", "transparent | off | server | client  (transparent/off is the safe choice for a single stack):", First(@"^vtp mode (\S+)", "transparent"));
        if (string.IsNullOrWhiteSpace(v)) return;
        await Apply("VTP mode", $"vtp mode {v.Trim()}");
    }

    private async void License_Click(object s, RoutedEventArgs e) { var r = await RunAsync("show license summary"); if (r != null) Dialogs.ShowText(this, "Licenses", r.Output); }
    private async void Boot_Click(object s, RoutedEventArgs e)
    {
        var r = await RunAsync("show boot"); if (r == null) return;
        var r2 = await RunAsync("dir flash:");
        Dialogs.ShowText(this, "Boot / flash", r.Output + "\n\n" + (r2?.Output ?? ""));
    }
}
