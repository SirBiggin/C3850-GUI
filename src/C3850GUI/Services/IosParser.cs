using System.Text.RegularExpressions;
using C3850GUI.Models;

namespace C3850GUI.Services;

/// <summary>Parsers for the fixed-width tables IOS-XE prints. Tolerant of column drift.</summary>
public static class IosParser
{
    private static IEnumerable<string> Lines(string s) => s.Replace("\r", "").Split('\n');

    // Port      Name               Status       Vlan       Duplex  Speed Type
    public static List<PortInfo> InterfacesStatus(string output)
    {
        var list = new List<PortInfo>();
        int cName = -1, cStatus = -1, cVlan = -1, cDuplex = -1, cSpeed = -1, cType = -1;
        foreach (var raw in Lines(output))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("Port") && line.Contains("Status"))
            {
                cName = line.IndexOf("Name", StringComparison.Ordinal);
                cStatus = line.IndexOf("Status", StringComparison.Ordinal);
                cVlan = line.IndexOf("Vlan", StringComparison.Ordinal);
                cDuplex = line.IndexOf("Duplex", StringComparison.Ordinal);
                cSpeed = line.IndexOf("Speed", StringComparison.Ordinal);
                cType = line.IndexOf("Type", StringComparison.Ordinal);
                continue;
            }
            if (cStatus < 0 || line.Length < cStatus || string.IsNullOrWhiteSpace(line)) continue;
            string Col(int a, int b) => a < 0 || a >= line.Length ? "" : (b < 0 || b > line.Length ? line[a..] : line[a..b]).Trim();
            var p = new PortInfo
            {
                Name = Col(0, cName),
                Description = Col(cName, cStatus),
                Status = Col(cStatus, cVlan),
                Vlan = Col(cVlan, cDuplex),
                Duplex = Col(cDuplex, cSpeed),
                Speed = Col(cSpeed, cType),
                Type = Col(cType, -1)
            };
            if (p.Name.Length > 0 && !p.Name.StartsWith('-')) list.Add(p);
        }
        return list;
    }

    public class SwitchportInfo
    {
        public string Name = ""; public string AdminMode = ""; public string OperMode = "";
        public string AccessVlan = ""; public string NativeVlan = ""; public string Allowed = ""; public string Voice = "";
        public bool Enabled = true;
    }

    /// <summary>"show interfaces switchport" → per-port admin/oper mode and VLAN settings, keyed by short name (Gi1/0/1).</summary>
    public static Dictionary<string, SwitchportInfo> InterfacesSwitchport(string output)
    {
        var d = new Dictionary<string, SwitchportInfo>(StringComparer.OrdinalIgnoreCase);
        SwitchportInfo? cur = null;
        foreach (var raw in Lines(output))
        {
            var l = raw.Trim();
            if (l.StartsWith("Name:")) { cur = new SwitchportInfo { Name = l[5..].Trim() }; d[cur.Name] = cur; continue; }
            if (cur == null) continue;
            string V(string key) => l.StartsWith(key) ? l[key.Length..].Trim() : null!;
            if (V("Switchport:") is { } sw) cur.Enabled = sw.StartsWith("Enabled", StringComparison.OrdinalIgnoreCase);
            else if (V("Administrative Mode:") is { } am) cur.AdminMode = am;
            else if (V("Operational Mode:") is { } om) cur.OperMode = om;
            else if (V("Access Mode VLAN:") is { } av) cur.AccessVlan = FirstToken(av);
            else if (V("Trunking Native Mode VLAN:") is { } nv) cur.NativeVlan = FirstToken(nv);
            else if (V("Voice VLAN:") is { } vv) cur.Voice = FirstToken(vv);
            else if (V("Trunking VLANs Enabled:") is { } te) cur.Allowed = te.Equals("ALL", StringComparison.OrdinalIgnoreCase) ? "all" : te.Replace(" ", "");
            else if (cur.Allowed.Length > 0 && cur.Allowed != "all" && Regex.IsMatch(l, @"^[\d,\-]+$")) cur.Allowed += l; // wrapped allowed list
        }
        return d;
    }

    private static string FirstToken(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    /// <summary>Collapse IOS's admin-mode wording to access | trunk | dynamic | routed.</summary>
    public static string SimplifyMode(SwitchportInfo sp)
    {
        if (!sp.Enabled) return "routed";
        var a = sp.AdminMode.ToLowerInvariant();
        if (a.Contains("trunk")) return "trunk";
        if (a.Contains("access")) return "access";
        if (a.Contains("dynamic")) return sp.OperMode.Contains("trunk", StringComparison.OrdinalIgnoreCase) ? "trunk" : "dynamic";
        return a.Length == 0 ? "" : a;
    }

    // VLAN Name                             Status    Ports
    public static List<VlanInfo> VlanBrief(string output)
    {
        var list = new List<VlanInfo>();
        VlanInfo? cur = null;
        foreach (var raw in Lines(output))
        {
            var m = Regex.Match(raw, @"^(\d+)\s+(.+?)\s+(active|act/lshut|act/unsup|suspended)\s*(.*)$");
            if (m.Success)
            {
                cur = new VlanInfo { Id = int.Parse(m.Groups[1].Value), Name = m.Groups[2].Value.Trim(), Status = m.Groups[3].Value, Ports = m.Groups[4].Value.Trim() };
                list.Add(cur);
            }
            else if (cur != null && raw.StartsWith("      ") && raw.Trim().Length > 0)
                cur.Ports = (cur.Ports + " " + raw.Trim()).Trim();
        }
        return list;
    }

    // Switch/Stack Mac Address : ...
    // Switch#   Role    Mac Address     Priority Version  State
    public static List<StackMember> ShowSwitch(string output)
    {
        var list = new List<StackMember>();
        foreach (var raw in Lines(output))
        {
            var m = Regex.Match(raw, @"^\*?\s*(\d+)\s+(Active|Standby|Member)\s+([0-9a-f\.]+)\s+(\d+)\s+(\S+)\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                list.Add(new StackMember
                {
                    Number = int.Parse(m.Groups[1].Value), Role = m.Groups[2].Value, Mac = m.Groups[3].Value,
                    Priority = int.Parse(m.Groups[4].Value), Version = m.Groups[5].Value, State = m.Groups[6].Value.Trim()
                });
        }
        return list;
    }

    public static (List<PoeModule> modules, List<PoePort> ports) PowerInline(string output)
    {
        var mods = new List<PoeModule>();
        var ports = new List<PoePort>();
        foreach (var raw in Lines(output))
        {
            var mm = Regex.Match(raw, @"^(\d+)\s+([\d\.]+)\s+([\d\.]+)\s+([\d\.]+)\s*$");
            if (mm.Success)
            {
                mods.Add(new PoeModule { Module = int.Parse(mm.Groups[1].Value), Available = double.Parse(mm.Groups[2].Value), Used = double.Parse(mm.Groups[3].Value), Remaining = double.Parse(mm.Groups[4].Value) });
                continue;
            }
            var pm = Regex.Match(raw, @"^(\S+/\d+)\s+(auto|static|off|never)\s+(\S+)\s+([\d\.]+)\s+(.+?)\s+(\S+)\s+([\d\.]+)\s*$", RegexOptions.IgnoreCase);
            if (pm.Success)
                ports.Add(new PoePort { Interface = pm.Groups[1].Value, Admin = pm.Groups[2].Value, Oper = pm.Groups[3].Value, Power = pm.Groups[4].Value, Device = pm.Groups[5].Value.Trim(), Class = pm.Groups[6].Value, Max = pm.Groups[7].Value });
        }
        return (mods, ports);
    }

    public static List<MacEntry> MacTable(string output)
    {
        var list = new List<MacEntry>();
        foreach (var raw in Lines(output))
        {
            var m = Regex.Match(raw, @"^\s*(\S+)\s+([0-9a-f]{4}\.[0-9a-f]{4}\.[0-9a-f]{4})\s+(\S+)\s+(\S+)\s*$", RegexOptions.IgnoreCase);
            if (m.Success) list.Add(new MacEntry { Vlan = m.Groups[1].Value, Mac = m.Groups[2].Value, Type = m.Groups[3].Value, Port = m.Groups[4].Value });
        }
        return list;
    }

    public static VersionInfo Version(string output)
    {
        var v = new VersionInfo();
        string G(string pat) { var m = Regex.Match(output, pat, RegexOptions.Multiline | RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value.Trim() : ""; }
        v.Version = G(@"Version\s+([\w\.\(\)]+)");
        v.Image = G(@"System image file is ""([^""]+)""");
        v.Hostname = G(@"^(\S+)\s+uptime is");
        v.Uptime = G(@"uptime is\s+(.+)$");
        v.Model = G(@"^Model [Nn]umber\s*:\s*(\S+)");
        v.Serial = G(@"^System [Ss]erial [Nn]umber\s*:\s*(\S+)");
        v.LastReload = G(@"Last reload reason:\s*(.+)$");
        v.ConfigRegister = G(@"Configuration register is\s+(\S+)");
        return v;
    }

    /// <summary>Parses "show processes cpu | include CPU" → (5s, 1m, 5m) percentages.</summary>
    public static (int five, int one, int fiveMin) Cpu(string output)
    {
        var m = Regex.Match(output, @"five seconds:\s*(\d+)%.*?one minute:\s*(\d+)%;\s*five minutes:\s*(\d+)%");
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : (0, 0, 0);
    }

    /// <summary>"show processes memory | include Processor" → (total, used) bytes.</summary>
    public static (long total, long used) Memory(string output)
    {
        var m = Regex.Match(output, @"Processor Pool Total:\s*(\d+)\s+Used:\s*(\d+)");
        return m.Success ? (long.Parse(m.Groups[1].Value), long.Parse(m.Groups[2].Value)) : (0, 0);
    }

    /// <summary>Extract per-interface counters from "show interfaces counters errors".</summary>
    public static Dictionary<string, (long inErr, long outErr)> CounterErrors(string output)
    {
        var d = new Dictionary<string, (long, long)>();
        foreach (var raw in Lines(output))
        {
            // Port  Align-Err  FCS-Err  Xmit-Err  Rcv-Err  UnderSize  OutDiscards
            var m = Regex.Match(raw, @"^(\S+/\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)");
            if (m.Success)
                d[m.Groups[1].Value] = (long.Parse(m.Groups[2].Value) + long.Parse(m.Groups[3].Value) + long.Parse(m.Groups[5].Value), long.Parse(m.Groups[4].Value) + long.Parse(m.Groups[7].Value));
        }
        return d;
    }
}
