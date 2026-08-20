using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using C3850GUI.Models;
using C3850GUI.Services;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

public partial class ExplorerPage : SwitchPage
{
    private CommandNode _root = new("", "", null);
    public record BatchItem(string[] Ctx, string Cmd)
    {
        public string Display => (Ctx.Length == 0 ? "" : "[" + Ctx[^1] + "] ") + Cmd;
    }
    private readonly ObservableCollection<BatchItem> _batch = new();
    private string _rootKey = "";          // which (switch, context) the tree was built for
    private bool _loadingRoot;

    public ExplorerPage()
    {
        InitializeComponent();
        Batch.ItemsSource = _batch;
        Batch.DisplayMemberPath = "Display";
    }

    // ------------------------------------------------------------ context / mode

    private string[] Context
    {
        get
        {
            var tag = (ModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            if (tag == "{custom}") return CustomContext.Text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tag.Length == 0) return Array.Empty<string>();
            var parts = tag.Split('|');
            return parts.Select(p => p.Contains("{0}") ? string.Format(p, ContextArg.Text.Trim()) : p).ToArray();
        }
    }

    private bool ContextReady()
    {
        var tag = (ModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        if (tag.Contains("{0}") && ContextArg.Text.Trim().Length == 0) return false;
        if (tag == "{custom}" && CustomContext.Text.Trim().Length == 0) return false;
        return true;
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ContextArg == null) return;
        var tag = (ModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        ContextArg.Visibility = tag.Contains("{0}") ? Visibility.Visible : Visibility.Collapsed;
        CustomContext.Visibility = tag == "{custom}" ? Visibility.Visible : Visibility.Collapsed;
        UpdateContextLabel();
        if (ContextReady() && Connected) _ = LoadRootAsync();
        else { _root = new CommandNode("", "", null); Tree.ItemsSource = _root.Children; }
    }

    private async void ContextArg_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ContextReady() && RequireConnection()) { UpdateContextLabel(); await LoadRootAsync(); }
    }

    private void UpdateContextLabel()
    {
        var ctx = Context;
        var host = Session?.Hostname ?? "Switch";
        var prompt = ctx.Length == 0 ? "#" : ctx[^1].StartsWith("interface range") ? "(config-if-range)#" : ctx[^1].StartsWith("interface") ? "(config-if)#" :
            ctx[^1].StartsWith("vlan") ? "(config-vlan)#" : ctx[^1].StartsWith("line") ? "(config-line)#" : ctx[^1].StartsWith("router") ? "(config-router)#" : "(config…)#";
        ContextLabel.Text = (ctx.Length == 0 ? "" : string.Join("  →  ", ctx) + "\n") + host + prompt;
    }

    // ------------------------------------------------------------ tree

    protected override async Task RefreshAsync()
    {
        UpdateContextLabel();
        if (ContextReady()) await LoadRootAsync();
    }

    private async void ReloadRoot_Click(object s, RoutedEventArgs e) { if (RequireConnection()) { _rootKey = ""; await LoadRootAsync(); } }

    private async Task LoadRootAsync()
    {
        if (!Connected || _loadingRoot) return;
        var key = Session!.Profile.Id + "|" + string.Join("|", Context);
        if (key == _rootKey && _root.Children.Count > 0) return;
        _loadingRoot = true;
        try
        {
            var entries = await Session.QueryHelpAsync(Context, "");
            _root = new CommandNode("", "", null);
            foreach (var en in entries) _root.Children.Add(new CommandNode(en.Token, en.Help, _root));
            _root.IsLoaded = true;
            _rootKey = key;
            Tree.ItemsSource = _root.Children;
            HelpList.ItemsSource = entries;
        }
        catch (Exception ex) { Toast($"Explorer: {ex.Message}", ControlAppearance.Danger, 8); }
        finally { _loadingRoot = false; }
    }

    private async Task LoadChildrenAsync(CommandNode node)
    {
        if (node.IsLoaded || node.IsLoading || node.IsTerminal || !Connected) return;
        node.IsLoading = true;
        try
        {
            var entries = await Session!.QueryHelpAsync(Context, node.Path + " ");
            node.Children.Clear();
            foreach (var en in entries) node.Children.Add(new CommandNode(en.Token, en.Help, node));
            node.IsLoaded = true;
        }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
        finally { node.IsLoading = false; }
    }

    private async void Tree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TreeViewItem { DataContext: CommandNode node }) await LoadChildrenAsync(node);
    }

    private async void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not CommandNode node) return;
        CommandLine.Text = node.Path + (node.IsArgument || !node.IsCr ? " " : "");
        CommandLine.CaretIndex = CommandLine.Text.Length;
        if (!node.IsTerminal)
        {
            await LoadChildrenAsync(node);
            HelpList.ItemsSource = node.Children.Select(c => new HelpEntry(c.Token, c.Help)).ToList();
        }
        else if (node.Parent != null)
            HelpList.ItemsSource = node.Parent.Children.Select(c => new HelpEntry(c.Token, c.Help)).ToList();
    }

    private async void Tree_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Tree.SelectedItem is CommandNode { IsCr: true }) await RunAsync();
    }

    private void TreeFilter_Changed(object sender, TextChangedEventArgs e)
    {
        var f = TreeFilter.Text.Trim();
        Tree.ItemsSource = f.Length == 0 ? _root.Children : Flatten(_root).Where(n => n.Token.Contains(f, StringComparison.OrdinalIgnoreCase) || n.Help.Contains(f, StringComparison.OrdinalIgnoreCase)).Take(500).ToList();
    }

    private static IEnumerable<CommandNode> Flatten(CommandNode n)
    {
        foreach (var c in n.Children)
        {
            if (ReferenceEquals(c, CommandNode.Placeholder)) continue;
            yield return c;
            foreach (var g in Flatten(c)) yield return g;
        }
    }

    // ------------------------------------------------------------ builder

    private async void Help_Click(object s, RoutedEventArgs e) => await ShowHelpAsync();

    private async Task ShowHelpAsync()
    {
        if (!RequireConnection()) return;
        try
        {
            var text = CommandLine.Text;
            var caret = CommandLine.CaretIndex;
            var prefix = text[..Math.Min(caret, text.Length)];
            var entries = await Session!.QueryHelpAsync(Context, prefix);
            HelpList.ItemsSource = entries;
            if (entries.Count == 1 && entries[0].Token == "% error") Toast(entries[0].Help, ControlAppearance.Caution);
        }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private void CommandLine_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == "?") { e.Handled = true; _ = ShowHelpAsync(); }
    }

    private async void CommandLine_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await RunAsync(); }
        else if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; await ShowHelpAsync(); }
    }

    private void HelpList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HelpList.SelectedItem is not HelpEntry h || h.Token.StartsWith('%')) return;
        if (h.Token == "<cr>") { _ = RunAsync(); return; }
        var t = CommandLine.Text;
        if (t.Length > 0 && !t.EndsWith(' ')) t += " ";
        // argument placeholders (WORD, <1-4094>) are hints — don't paste them literally
        CommandLine.Text = t + (h.Token.StartsWith('<') || h.Token.All(c => char.IsUpper(c) || !char.IsLetter(c)) ? "" : h.Token + " ");
        CommandLine.CaretIndex = CommandLine.Text.Length;
        CommandLine.Focus();
        if (!h.Token.StartsWith('<')) _ = ShowHelpAsync();
    }

    private async Task RunAsync()
    {
        var cmd = CommandLine.Text.Trim();
        if (cmd.Length == 0 || !RequireConnection()) return;
        var ctx = Context;
        try
        {
            CommandResult r;
            if (ctx.Length == 0)
            {
                r = await Session!.RunAsync(cmd, default, TimeSpan.FromSeconds(45));
            }
            else
            {
                if (App.Store.Settings.ConfirmConfigCommands &&
                    !Dialogs.Confirm(this, "Run in configuration mode", string.Join("\n", ctx) + "\n  " + cmd + "\nend")) return;
                r = await Session!.RunSequenceAsync(ctx.Append(cmd).Append("end"));
            }
            Output.AppendText($"{Session!.Hostname}{(ctx.Length == 0 ? "#" : "(config)#")} {cmd}\n{r.Output}\n\n");
            Output.ScrollToEnd();
            if (r.Error) Toast(r.ErrorText, ControlAppearance.Caution, 6);
        }
        catch (TimeoutException)
        {
            Toast("No prompt came back — the switch may be waiting for input. Check the Terminal page to answer it.", ControlAppearance.Caution, 10);
        }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    private async void Run_Click(object s, RoutedEventArgs e) => await RunAsync();

    private void AddBatch_Click(object s, RoutedEventArgs e)
    {
        var cmd = CommandLine.Text.Trim();
        if (cmd.Length == 0) return;
        _batch.Add(new BatchItem(Context, cmd));
        CommandLine.Text = "";
    }

    private void Batch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Batch.SelectedIndex >= 0) _batch.RemoveAt(Batch.SelectedIndex);
    }

    private void ClearBatch_Click(object s, RoutedEventArgs e) => _batch.Clear();

    private async void RunBatch_Click(object s, RoutedEventArgs e)
    {
        if (_batch.Count == 0 || !RequireConnection()) return;
        var preview = string.Join("\n", _batch.Select(b => (b.Ctx.Length == 0 ? "" : "[" + string.Join(" / ", b.Ctx) + "] ") + b.Cmd));
        if (!Dialogs.Confirm(this, $"Run {_batch.Count} command(s)", preview)) return;
        foreach (var (ctx, cmd) in _batch.Select(b => (b.Ctx, b.Cmd)).ToList())
        {
            try
            {
                var r = ctx.Length == 0 ? await Session!.RunAsync(cmd) : await Session!.RunSequenceAsync(ctx.Append(cmd).Append("end"));
                Output.AppendText($"> {cmd}\n{r.Output}\n\n");
                if (r.Error) { Toast($"Stopped at '{cmd}': {r.ErrorText}", ControlAppearance.Danger, 8); break; }
            }
            catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); break; }
        }
        Output.ScrollToEnd();
    }

    private void CopyOut_Click(object s, RoutedEventArgs e) { if (Output.Text.Length > 0) Clipboard.SetText(Output.Text); }
    private void ClearOut_Click(object s, RoutedEventArgs e) => Output.Clear();
}
