using System.Windows;
using System.Windows.Controls;
using C3850GUI.Models;
using C3850GUI.Services;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

public partial class MacTablePage : SwitchPage
{
    private List<MacEntry> _all = new();
    public MacTablePage() { InitializeComponent(); }

    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (RequireConnection()) await SafeRefreshAsync(); }

    protected override async Task RefreshAsync()
    {
        _all = IosParser.MacTable((await Session!.RunAsync("show mac address-table")).Output);
        Sub.Text = $"{_all.Count} entries  ·  {_all.Count(m => m.Type.Equals("DYNAMIC", StringComparison.OrdinalIgnoreCase))} dynamic";
        Apply();
    }

    private void Filter_TextChanged(object s, TextChangedEventArgs e) => Apply();
    private void Apply()
    {
        var f = FilterBox.Text.Trim().Replace("-", "").Replace(":", "").ToLowerInvariant();
        Grid.ItemsSource = f.Length == 0 ? _all : _all.Where(m => (m.Vlan + " " + m.Mac.Replace(".", "") + " " + m.Type + " " + m.Port).ToLowerInvariant().Contains(f)).ToList();
    }

    private async void Show(string cmd, string title) { var r = await RunAsync(cmd); if (r != null) Dialogs.ShowText(this, title, r.Output); }
    private void Arp_Click(object s, RoutedEventArgs e) => Show("show ip arp", "ARP");
    private void Cdp_Click(object s, RoutedEventArgs e) => Show("show cdp neighbors detail", "CDP neighbors");
    private void Lldp_Click(object s, RoutedEventArgs e) => Show("show lldp neighbors detail", "LLDP neighbors");
    private void Snoop_Click(object s, RoutedEventArgs e) => Show("show ip dhcp snooping binding", "DHCP snooping bindings");

    private async void Clear_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection() || !Dialogs.Confirm(this, "Clear MAC table", "clear mac address-table dynamic")) return;
        try { await Session!.RunAsync("clear mac address-table dynamic"); await SafeRefreshAsync(); }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }
}
