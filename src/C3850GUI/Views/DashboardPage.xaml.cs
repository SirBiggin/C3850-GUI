using System.Windows;
using C3850GUI.Services;

namespace C3850GUI.Views;

public partial class DashboardPage : SwitchPage
{
    public DashboardPage() { InitializeComponent(); }

    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (RequireConnection()) await SafeRefreshAsync(); }

    protected override async Task RefreshAsync()
    {
        var s = Session!;
        HostTitle.Text = s.Hostname;
        HostSub.Text = $"{s.Profile.Name}  ·  {s.Profile.Host}";

        var ver = IosParser.Version((await s.RunAsync("show version")).Output);
        SysHost.Text = ver.Hostname.Length > 0 ? ver.Hostname : s.Hostname;
        SysModel.Text = ver.Model; SysVersion.Text = ver.Version; SysImage.Text = ver.Image;
        SysUptime.Text = ver.Uptime; SysReload.Text = ver.LastReload; SysSerial.Text = ver.Serial;

        var ports = IosParser.InterfacesStatus((await s.RunAsync("show interfaces status")).Output);
        StatPortsUp.Text = ports.Count(p => p.IsUp).ToString();
        StatPortsTotal.Text = ports.Count.ToString();
        StatErrDisabled.Text = ports.Count(p => p.IsErrDisabled).ToString();

        var members = IosParser.ShowSwitch((await s.RunAsync("show switch")).Output);
        StatMembers.Text = members.Count.ToString();
        StackList.ItemsSource = members;

        var cpu = IosParser.Cpu((await s.RunAsync("show processes cpu | include CPU utilization")).Output);
        StatCpu.Text = $"{cpu.five}% / {cpu.one}% / {cpu.fiveMin}%";

        var mem = IosParser.Memory((await s.RunAsync("show processes memory | include Processor Pool")).Output);
        StatMem.Text = mem.total > 0 ? $"{mem.used * 100 / mem.total}%" : "n/a";

        var (mods, _) = IosParser.PowerInline((await s.RunAsync("show power inline")).Output);
        StatPoe.Text = mods.Count > 0 ? $"{mods.Sum(m => m.Used):0} / {mods.Sum(m => m.Available):0}" : "n/a";

        var log = (await s.RunAsync("show logging | tail 40")).Output;
        if (log.StartsWith("%")) log = (await s.RunAsync("show logging")).Output; // older builds lack "| tail"
        var lines = log.Split('\n');
        RecentLog.Text = string.Join('\n', lines.Skip(Math.Max(0, lines.Length - 40)));
        RecentLog.ScrollToEnd();
    }
}
