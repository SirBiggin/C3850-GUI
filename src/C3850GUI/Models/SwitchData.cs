using CommunityToolkit.Mvvm.ComponentModel;

namespace C3850GUI.Models;

public partial class PortInfo : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _vlan = "";
    [ObservableProperty] private string _duplex = "";
    [ObservableProperty] private string _speed = "";
    [ObservableProperty] private string _type = "";
    [ObservableProperty] private bool _isSelected;

    // from "show interfaces switchport"
    [ObservableProperty] private string _mode = "";          // access | trunk | dynamic | routed | ""
    [ObservableProperty] private string _operMode = "";
    [ObservableProperty] private string _accessVlan = "";
    [ObservableProperty] private string _nativeVlan = "";
    [ObservableProperty] private string _allowedVlans = "";
    [ObservableProperty] private string _voiceVlan = "";

    public bool IsTrunk => Mode == "trunk";
    public string ModeDetail => Mode switch
    {
        "trunk" => $"native {NativeVlan}  allowed {AllowedVlans}",
        "access" => VoiceVlan.Length > 0 && VoiceVlan != "none" ? $"vlan {AccessVlan}  voice {VoiceVlan}" : $"vlan {AccessVlan}",
        "dynamic" => $"oper {OperMode}",
        _ => ""
    };

    public bool IsUp => Status.Equals("connected", StringComparison.OrdinalIgnoreCase);
    public bool IsDisabled => Status.Contains("disabled", StringComparison.OrdinalIgnoreCase);
    public bool IsErrDisabled => Status.Contains("err-disabled", StringComparison.OrdinalIgnoreCase);
    public string ShortName => Name
        .Replace("TenGigabitEthernet", "Te").Replace("GigabitEthernet", "Gi")
        .Replace("FortyGigabitEthernet", "Fo").Replace("TwentyFiveGigE", "Twe")
        .Replace("Port-channel", "Po").Replace("AppGigabitEthernet", "Ap");
}

public class VlanInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Ports { get; set; } = "";
}

public class StackMember
{
    public int Number { get; set; }
    public string Role { get; set; } = "";
    public string Mac { get; set; } = "";
    public int Priority { get; set; }
    public string Version { get; set; } = "";
    public string State { get; set; } = "";
}

public class PoePort
{
    public string Interface { get; set; } = "";
    public string Admin { get; set; } = "";
    public string Oper { get; set; } = "";
    public string Power { get; set; } = "";
    public string Device { get; set; } = "";
    public string Class { get; set; } = "";
    public string Max { get; set; } = "";
}

public class PoeModule
{
    public int Module { get; set; }
    public double Available { get; set; }
    public double Used { get; set; }
    public double Remaining { get; set; }
}

public class MacEntry
{
    public string Vlan { get; set; } = "";
    public string Mac { get; set; } = "";
    public string Type { get; set; } = "";
    public string Port { get; set; } = "";
}

public class VersionInfo
{
    public string Hostname { get; set; } = "";
    public string Version { get; set; } = "";
    public string Image { get; set; } = "";
    public string Uptime { get; set; } = "";
    public string Model { get; set; } = "";
    public string Serial { get; set; } = "";
    public string LastReload { get; set; } = "";
    public string ConfigRegister { get; set; } = "";
}
