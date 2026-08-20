using System.Windows;
using C3850GUI.Models;
using C3850GUI.Services;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

public partial class VlansPage : SwitchPage
{
    public VlansPage() { InitializeComponent(); }

    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (RequireConnection()) await SafeRefreshAsync(); }

    protected override async Task RefreshAsync()
    {
        var r = await Session!.RunAsync("show vlan brief");
        Grid.ItemsSource = IosParser.VlanBrief(r.Output);
    }

    private VlanInfo? SelectedVlan()
    {
        if (Grid.SelectedItem is VlanInfo v) return v;
        Toast("Select a VLAN first.", ControlAppearance.Caution);
        return null;
    }

    private async void Add_Click(object s, RoutedEventArgs e)
    {
        var f = Dialogs.Form(this, "Add VLAN", ("VLAN ID", "", "2-4094"), ("Name", "", "optional"));
        if (f == null || !int.TryParse(f["VLAN ID"], out var id)) return;
        var lines = new List<string> { $"vlan {id}" };
        if (!string.IsNullOrWhiteSpace(f["Name"])) lines.Add($"name {f["Name"].Trim()}");
        lines.Add("exit");
        if (await ConfigureAsync($"Add VLAN {id}", lines.ToArray())) await SafeRefreshAsync();
    }

    private async void Rename_Click(object s, RoutedEventArgs e)
    {
        var v = SelectedVlan(); if (v == null) return;
        var n = Dialogs.Prompt(this, $"Rename VLAN {v.Id}", "New name:", v.Name);
        if (string.IsNullOrWhiteSpace(n)) return;
        if (await ConfigureAsync($"Rename VLAN {v.Id}", $"vlan {v.Id}", $"name {n.Trim()}", "exit")) await SafeRefreshAsync();
    }

    private async void Delete_Click(object s, RoutedEventArgs e)
    {
        var v = SelectedVlan(); if (v == null) return;
        if (v.Id == 1 || (v.Id >= 1002 && v.Id <= 1005)) { Toast("That VLAN can't be deleted.", ControlAppearance.Caution); return; }
        if (!Dialogs.Confirm(this, "Delete VLAN", $"Delete VLAN {v.Id} ({v.Name})?\nPorts assigned to it will go inactive.", "Delete", true)) return;
        if (await ConfigureAsync($"Delete VLAN {v.Id}", $"no vlan {v.Id}")) await SafeRefreshAsync();
    }

    private async void Svi_Click(object s, RoutedEventArgs e)
    {
        var r = await RunAsync("show ip interface brief"); if (r == null) return;
        Dialogs.ShowText(this, "IP interfaces", r.Output);
    }

    private async void CreateSvi_Click(object s, RoutedEventArgs e)
    {
        var sel = Grid.SelectedItem as VlanInfo;
        var f = Dialogs.Form(this, "Create / edit SVI", ("VLAN ID", sel?.Id.ToString() ?? "", ""), ("IP address", "", "e.g. 10.214.20.1"), ("Subnet mask", "255.255.255.0", ""), ("Description", sel?.Name ?? "", "optional"));
        if (f == null || !int.TryParse(f["VLAN ID"], out var id)) return;
        var lines = new List<string> { $"interface Vlan{id}" };
        if (!string.IsNullOrWhiteSpace(f["Description"])) lines.Add($"description {f["Description"].Trim()}");
        if (!string.IsNullOrWhiteSpace(f["IP address"])) lines.Add($"ip address {f["IP address"].Trim()} {f["Subnet mask"].Trim()}");
        lines.Add("no shutdown"); lines.Add("exit");
        await ConfigureAsync($"SVI Vlan{id}", lines.ToArray());
    }

    private async void Trunks_Click(object s, RoutedEventArgs e)
    {
        var r = await RunAsync("show interfaces trunk"); if (r == null) return;
        Dialogs.ShowText(this, "Trunks", r.Output);
    }

    private async void Stp_Click(object s, RoutedEventArgs e)
    {
        var r = await RunAsync("show spanning-tree summary"); if (r == null) return;
        var r2 = await RunAsync("show spanning-tree root");
        Dialogs.ShowText(this, "Spanning tree", r.Output + "\n\n" + (r2?.Output ?? ""));
    }
}
