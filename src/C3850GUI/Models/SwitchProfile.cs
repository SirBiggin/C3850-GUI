using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace C3850GUI.Models;

public enum AuthMode { Password, PrivateKey }

/// <summary>A saved connection to one switch stack's management address.</summary>
public partial class SwitchProfile : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = "New Stack";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private int _port = 22;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private AuthMode _auth = AuthMode.Password;
    [ObservableProperty] private string _privateKeyPath = "";

    /// <summary>DPAPI-protected, base64. Never the clear password.</summary>
    [ObservableProperty] private string _protectedPassword = "";
    [ObservableProperty] private string _protectedEnableSecret = "";
    [ObservableProperty] private string _protectedKeyPassphrase = "";

    [ObservableProperty] private string _accentHex = "#2E8BFF";

    /// <summary>Front-panel layout per stack member: "stacked" (odd ports top row, even bottom — C3850 default) or "row".</summary>
    public Dictionary<int, string> MemberLayouts { get; set; } = new();
    /// <summary>Ports per faceplate group (a visual gap is drawn between groups). 0 = no grouping.</summary>
    [ObservableProperty] private int _portGroupSize = 12;
    [ObservableProperty] private string _notes = "";

    [JsonIgnore] public string Display => string.IsNullOrWhiteSpace(Name) ? Host : $"{Name}  ({Host})";

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Display));
    partial void OnHostChanged(string value) => OnPropertyChanged(nameof(Display));

    public SwitchProfile Clone() => new()
    {
        Id = Id, Name = Name, Host = Host, Port = Port, Username = Username, Auth = Auth,
        PrivateKeyPath = PrivateKeyPath, ProtectedPassword = ProtectedPassword,
        ProtectedEnableSecret = ProtectedEnableSecret, ProtectedKeyPassphrase = ProtectedKeyPassphrase,
        AccentHex = AccentHex, Notes = Notes, PortGroupSize = PortGroupSize,
        MemberLayouts = new Dictionary<int, string>(MemberLayouts)
    };
}
