using System.Windows;
using C3850GUI.Models;
using C3850GUI.Services;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

public partial class PoePage : SwitchPage
{
    public PoePage() { InitializeComponent(); }

    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (RequireConnection()) await SafeRefreshAsync(); }

    protected override async Task RefreshAsync()
    {
        var (mods, ports) = IosParser.PowerInline((await Session!.RunAsync("show power inline")).Output);
        Modules.ItemsSource = mods;
        Grid.ItemsSource = ports;
    }

    private List<PoePort>? Sel()
    {
        var s = Grid.SelectedItems.Cast<PoePort>().ToList();
        if (s.Count == 0) Toast("Select one or more ports first.", ControlAppearance.Caution);
        return s.Count == 0 ? null : s;
    }

    private static string[] PerPort(IEnumerable<PoePort> ports, string line) =>
        ports.SelectMany(p => new[] { $"interface {p.Interface}", line, "exit" }).ToArray();

    private async void Auto_Click(object s, RoutedEventArgs e)
    { var sel = Sel(); if (sel != null && await ConfigureAsync("PoE auto", PerPort(sel, "power inline auto"))) await SafeRefreshAsync(); }

    private async void Never_Click(object s, RoutedEventArgs e)
    { var sel = Sel(); if (sel != null && await ConfigureAsync("PoE never", PerPort(sel, "power inline never"))) await SafeRefreshAsync(); }

    private async void Cycle_Click(object s, RoutedEventArgs e)
    {
        var sel = Sel(); if (sel == null) return;
        if (!await ConfigureAsync("PoE power-cycle", PerPort(sel, "power inline never"))) return;
        await Task.Delay(3000);
        if (!RequireConnection()) return;
        var r = await Session!.ConfigureAsync(PerPort(sel, "power inline auto"));
        Toast(r.Error ? r.ErrorText : "Devices power-cycled", r.Error ? ControlAppearance.Danger : ControlAppearance.Success);
        await SafeRefreshAsync();
    }

    private async void Detail_Click(object s, RoutedEventArgs e)
    {
        var sel = Sel(); if (sel == null) return;
        var sb = new System.Text.StringBuilder();
        foreach (var p in sel.Take(10))
        {
            var r = await RunAsync($"show power inline {p.Interface} detail"); if (r == null) return;
            sb.AppendLine($"===== {p.Interface} =====").AppendLine(r.Output).AppendLine();
        }
        Dialogs.ShowText(this, "PoE detail", sb.ToString());
    }

    private async void Police_Click(object s, RoutedEventArgs e)
    {
        var r = await RunAsync("show power inline police"); if (r == null) return;
        Dialogs.ShowText(this, "PoE policing", r.Output);
    }
}
