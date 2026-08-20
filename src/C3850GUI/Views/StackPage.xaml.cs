using System.Windows;
using C3850GUI.Models;
using C3850GUI.Services;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

public partial class StackPage : SwitchPage
{
    public StackPage() { InitializeComponent(); }

    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (RequireConnection()) await SafeRefreshAsync(); }

    protected override async Task RefreshAsync()
    {
        var s = Session!;
        Grid.ItemsSource = IosParser.ShowSwitch((await s.RunAsync("show switch")).Output);
        StackPorts.Text = (await s.RunAsync("show switch stack-ports")).Output;
        Redundancy.Text = (await s.RunAsync("show redundancy")).Output;
        Env.Text = (await s.RunAsync("show environment all")).Output;
        Inventory.Text = (await s.RunAsync("show inventory")).Output;
    }

    private StackMember? SelectedMember()
    {
        if (Grid.SelectedItem is StackMember m) return m;
        Toast("Select a stack member first.", ControlAppearance.Caution);
        return null;
    }

    private async void Priority_Click(object s, RoutedEventArgs e)
    {
        var m = SelectedMember(); if (m == null) return;
        var v = Dialogs.Prompt(this, $"Switch {m.Number} priority", "New priority (1-15, higher wins active election):", m.Priority.ToString());
        if (v == null || !int.TryParse(v, out var p) || p < 1 || p > 15) return;
        if (!Dialogs.Confirm(this, "Set priority", $"switch {m.Number} priority {p}\n\nTakes effect at next election/reload.")) return;
        try
        {
            var r = await Session!.RunInteractiveAsync($"switch {m.Number} priority {p}", "yes");
            Toast(r.Error ? r.ErrorText : "Priority set", r.Error ? ControlAppearance.Danger : ControlAppearance.Success);
            await SafeRefreshAsync();
        }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private async void Renumber_Click(object s, RoutedEventArgs e)
    {
        var m = SelectedMember(); if (m == null) return;
        var v = Dialogs.Prompt(this, $"Renumber switch {m.Number}", "New member number (1-9). Requires a reload of that member to take effect:", m.Number.ToString());
        if (v == null || !int.TryParse(v, out var n) || n < 1 || n > 9 || n == m.Number) return;
        if (!Dialogs.Confirm(this, "Renumber", $"switch {m.Number} renumber {n}\n\nInterface names on that member will change after reload.", "Renumber", true)) return;
        try
        {
            var r = await Session!.RunInteractiveAsync($"switch {m.Number} renumber {n}", "yes");
            Toast(r.Error ? r.ErrorText : "Renumbered (reload member to apply)", r.Error ? ControlAppearance.Danger : ControlAppearance.Success);
        }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private async void ReloadMember_Click(object s, RoutedEventArgs e)
    {
        var m = SelectedMember(); if (m == null) return;
        if (!Dialogs.Confirm(this, "Reload member", $"reload slot {m.Number}\n\nSwitch {m.Number} will reboot now. Unsaved config on the stack is NOT saved by this action.", "Reload", true)) return;
        try
        {
            var r = await Session!.RunInteractiveAsync($"reload slot {m.Number}", "", default, TimeSpan.FromSeconds(30));
            Toast(r.Error ? r.ErrorText : $"Reload of switch {m.Number} issued", r.Error ? ControlAppearance.Danger : ControlAppearance.Success);
        }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private async void Switchover_Click(object s, RoutedEventArgs e)
    {
        if (!Dialogs.Confirm(this, "Redundancy switchover", "redundancy force-switchover\n\nThe standby becomes active; this SSH session will drop.", "Switch over", true)) return;
        try { await Session!.RunInteractiveAsync("redundancy force-switchover", "", default, TimeSpan.FromSeconds(20)); }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Caution); }
    }

    private async void Save_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection()) return;
        try
        {
            var r = await Session!.RunInteractiveAsync("write memory", "", default, TimeSpan.FromSeconds(60));
            Toast(r.Output.Contains("[OK]") ? "Configuration saved" : r.Output, r.Output.Contains("[OK]") ? ControlAppearance.Success : ControlAppearance.Caution, 6);
        }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private async void ReloadStack_Click(object s, RoutedEventArgs e)
    {
        if (!RequireConnection()) return;
        var typed = Dialogs.Prompt(this, "Reload entire stack", "Type RELOAD to confirm. Config will be saved first (write memory).", "");
        if (typed != "RELOAD") return;
        try
        {
            await Session!.RunInteractiveAsync("write memory", "", default, TimeSpan.FromSeconds(60));
            await Session!.RunInteractiveAsync("reload", "", default, TimeSpan.FromSeconds(20));
        }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Caution); }
    }
}
