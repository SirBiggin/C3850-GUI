using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using C3850GUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace C3850GUI.Views;

public class TrunkDialogResult
{
    public string Interfaces = "";
    public string Allowed = "all";
    public int Native = 1;
    public string Description = "";
    // port-channel only
    public int Channel = 1;
    public string Mode = "active";
}

/// <summary>
/// Trunk / Port-channel editor. Allowed VLANs are picked from the switch's real VLAN list
/// (show vlan brief), native VLAN from a dropdown of the same list.
/// </summary>
public static partial class TrunkDialog
{
    private partial class VlanPick : ObservableObject
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        [ObservableProperty] private bool _checked;
        public string Label => $"{Id,-5} {Name}";
    }

    public static TrunkDialogResult? Show(DependencyObject owner, string title, List<VlanInfo> vlans, string interfaces,
        bool portChannel, string currentAllowed = "all", int currentNative = 1)
    {
        var picks = new ObservableCollection<VlanPick>(vlans.OrderBy(v => v.Id).Select(v => new VlanPick { Id = v.Id, Name = v.Name }));
        // pre-check whatever's currently allowed (if not "all")
        if (currentAllowed != "all") foreach (var p in picks) p.Checked = PortsPage.VlanListContains(currentAllowed, p.Id);

        var root = new StackPanel { Margin = new Thickness(22, 14, 22, 18) };
        root.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });

        TextBox Field(string label, string initial, string placeholder = "")
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.8 });
            var tb = new TextBox { Text = initial, PlaceholderText = placeholder, Margin = new Thickness(0, 4, 0, 0) };
            root.Children.Add(tb);
            return tb;
        }

        var ifBox = Field(portChannel ? "Member interfaces (IOS range syntax)" : "Interfaces (IOS range syntax)", interfaces, "e.g. te2/0/6-7  or  gi1/0/1-4,gi1/0/10");
        TextBox? chBox = null, modeBox = null;
        if (portChannel)
        {
            var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var s1 = new StackPanel(); s1.Children.Add(new TextBlock { Text = "Channel number", Opacity = 0.8 });
            chBox = new TextBox { Text = "1", Margin = new Thickness(0, 4, 0, 0) }; s1.Children.Add(chBox);
            var s2 = new StackPanel(); Grid.SetColumn(s2, 2); s2.Children.Add(new TextBlock { Text = "Mode", Opacity = 0.8 });
            var modeCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var m in new[] { "active  (LACP)", "passive  (LACP)", "desirable  (PAgP)", "auto  (PAgP)", "on  (static, no negotiation)" }) modeCombo.Items.Add(m);
            modeCombo.SelectedIndex = 0; s2.Children.Add(modeCombo);
            modeBox = new TextBox { Visibility = Visibility.Collapsed }; // holder; read from combo instead
            modeCombo.SelectionChanged += (_, _) => modeBox.Text = modeCombo.SelectedItem?.ToString()?.Split(' ')[0] ?? "active";
            modeBox.Text = "active";
            g.Children.Add(s1); g.Children.Add(s2);
            root.Children.Add(g);
        }

        // ---- allowed VLANs
        var hdr = new Grid { Margin = new Thickness(0, 12, 0, 4) };
        hdr.ColumnDefinitions.Add(new ColumnDefinition()); hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hdr.Children.Add(new TextBlock { Text = "Allowed VLANs", Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center });
        var allToggle = new CheckBox { Content = "All VLANs", IsChecked = currentAllowed == "all" };
        Grid.SetColumn(allToggle, 1); hdr.Children.Add(allToggle);
        root.Children.Add(hdr);

        var search = new TextBox { PlaceholderText = "Filter by ID or name", Margin = new Thickness(0, 0, 0, 4) };
        root.Children.Add(search);
        var list = new ListBox { Height = 260, SelectionMode = SelectionMode.Single, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5 };
        list.ItemTemplate = MakeCheckTemplate();
        var compact = new Style(typeof(ListBoxItem), (Style)list.FindResource(typeof(ListBoxItem)));
        compact.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 0, 6, 0)));
        compact.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0.0));
        compact.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        list.ItemContainerStyle = compact;
        list.ItemsSource = picks;
        root.Children.Add(list);
        var summary = new TextBlock { Margin = new Thickness(0, 6, 0, 0), Opacity = 0.8, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        root.Children.Add(summary);

        var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var selAll = new Button { Content = "Check all", Padding = new Thickness(10, 4, 10, 4) };
        var selNone = new Button { Content = "Uncheck all", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        quick.Children.Add(selAll); quick.Children.Add(selNone);
        root.Children.Add(quick);

        // ---- native VLAN
        root.Children.Add(new TextBlock { Text = "Native VLAN (untagged)", Margin = new Thickness(0, 12, 0, 0), Opacity = 0.8 });
        var native = new ComboBox { Margin = new Thickness(0, 4, 0, 0), FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas") };
        foreach (var p in picks) native.Items.Add(p);
        native.DisplayMemberPath = nameof(VlanPick.Label);
        native.SelectedItem = picks.FirstOrDefault(p => p.Id == currentNative) ?? picks.FirstOrDefault(p => p.Id == 1) ?? picks.FirstOrDefault();
        root.Children.Add(native);
        var nativeWarn = new TextBlock { Foreground = System.Windows.Media.Brushes.Orange, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        root.Children.Add(nativeWarn);

        var descBox = Field("Description (optional)", "");

        // ---- buttons
        var ok = new Button { Content = "Apply", Appearance = ControlAppearance.Primary, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        root.Children.Add(btns);

        var w = new FluentWindow
        {
            Title = title, Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ExtendsContentIntoTitleBar = true,
            WindowBackdropType = WindowBackdropType.Mica, ShowInTaskbar = false,
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 900 },
            Owner = Window.GetWindow(owner) ?? Application.Current.MainWindow
        };

        // ---- behaviour
        string AllowedText() => allToggle.IsChecked == true ? "all" : Compress(picks.Where(p => p.Checked).Select(p => p.Id));
        void Refresh()
        {
            var all = allToggle.IsChecked == true;
            list.IsEnabled = !all; search.IsEnabled = !all; selAll.IsEnabled = !all; selNone.IsEnabled = !all;
            var txt = AllowedText();
            summary.Text = "switchport trunk allowed vlan " + (txt.Length == 0 ? "(none)" : txt);
            var nat = (native.SelectedItem as VlanPick)?.Id ?? 1;
            nativeWarn.Visibility = (!all && txt.Length > 0 && !PortsPage.VlanListContains(txt, nat)) ? Visibility.Visible : Visibility.Collapsed;
            nativeWarn.Text = $"VLAN {nat} is the native VLAN but isn't in the allowed list — untagged traffic will be dropped.";
        }
        allToggle.Checked += (_, _) => Refresh(); allToggle.Unchecked += (_, _) => Refresh();
        native.SelectionChanged += (_, _) => Refresh();
        foreach (var p in picks) p.PropertyChanged += (_, _) => Refresh();
        selAll.Click += (_, _) => { foreach (var p in list.Items.OfType<VlanPick>()) p.Checked = true; };
        selNone.Click += (_, _) => { foreach (var p in picks) p.Checked = false; };
        search.TextChanged += (_, _) =>
        {
            var q = search.Text.Trim();
            list.ItemsSource = q.Length == 0 ? picks : new ObservableCollection<VlanPick>(picks.Where(p => p.Id.ToString().Contains(q) || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)));
        };
        Refresh();

        TrunkDialogResult? result = null;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(ifBox.Text)) { ifBox.Focus(); return; }
            var allowed = AllowedText();
            if (allowed.Length == 0) { summary.Text = "Pick at least one VLAN, or tick All VLANs."; return; }
            var r = new TrunkDialogResult
            {
                Interfaces = ifBox.Text.Trim(), Allowed = allowed,
                Native = (native.SelectedItem as VlanPick)?.Id ?? 1, Description = descBox.Text.Trim()
            };
            if (portChannel)
            {
                if (!int.TryParse(chBox!.Text, out var ch) || ch < 1 || ch > 128) { chBox.Focus(); return; }
                r.Channel = ch; r.Mode = modeBox!.Text;
            }
            result = r; w.DialogResult = true; w.Close();
        };
        cancel.Click += (_, _) => { w.DialogResult = false; w.Close(); };
        w.Loaded += (_, _) => { if (ifBox.Text.Length == 0) ifBox.Focus(); };
        return w.ShowDialog() == true ? result : null;
    }

    private static DataTemplate MakeCheckTemplate()
    {
        var f = new FrameworkElementFactory(typeof(CheckBox));
        f.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(VlanPick.Checked)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        f.SetBinding(CheckBox.ContentProperty, new System.Windows.Data.Binding(nameof(VlanPick.Label)));
        f.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
        return new DataTemplate { VisualTree = f };
    }

    /// <summary>Access-port editor: access VLAN + optional voice VLAN, both chosen from the switch's VLAN list.</summary>
    public static (int access, int voice)? ShowAccess(DependencyObject owner, List<VlanInfo> vlans, int currentAccess, int currentVoice, int count)
    {
        var items = vlans.OrderBy(v => v.Id).Select(v => new VlanPick { Id = v.Id, Name = v.Name }).ToList();
        VlanPick? Find(int id) => items.FirstOrDefault(p => p.Id == id);

        var root = new StackPanel { Margin = new Thickness(22, 14, 22, 18) };
        root.Children.Add(new TextBlock { Text = count > 1 ? $"Access VLAN — {count} ports" : "Access VLAN", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });

        root.Children.Add(new TextBlock { Text = "Access (data) VLAN", Opacity = 0.8 });
        var access = new ComboBox { Margin = new Thickness(0, 4, 0, 0), FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), DisplayMemberPath = nameof(VlanPick.Label) };
        foreach (var p in items) access.Items.Add(p);
        access.SelectedItem = Find(currentAccess) ?? Find(1) ?? items.FirstOrDefault();
        root.Children.Add(access);

        root.Children.Add(new TextBlock { Text = "Voice VLAN (optional)", Opacity = 0.8, Margin = new Thickness(0, 12, 0, 0) });
        var voice = new ComboBox { Margin = new Thickness(0, 4, 0, 0), FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), DisplayMemberPath = nameof(VlanPick.Label) };
        var none = new VlanPick { Id = 0, Name = "(none)" };
        voice.Items.Add(none);
        foreach (var p in items) voice.Items.Add(p);
        voice.SelectedItem = currentVoice > 0 ? Find(currentVoice) ?? none : none;
        root.Children.Add(voice);

        var ok = new Button { Content = "Apply", Appearance = ControlAppearance.Primary, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        root.Children.Add(btns);

        var w = new FluentWindow
        {
            Title = "Access VLAN", Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ExtendsContentIntoTitleBar = true,
            WindowBackdropType = WindowBackdropType.Mica, ShowInTaskbar = false, Content = root,
            Owner = Window.GetWindow(owner) ?? Application.Current.MainWindow
        };
        (int, int)? result = null;
        ok.Click += (_, _) => { result = ((access.SelectedItem as VlanPick)?.Id ?? 1, (voice.SelectedItem as VlanPick)?.Id ?? 0); w.DialogResult = true; w.Close(); };
        cancel.Click += (_, _) => { w.DialogResult = false; w.Close(); };
        return w.ShowDialog() == true ? result : null;
    }

    /// <summary>1,2,3,5,10,11,12 → "1-3,5,10-12" (IOS accepts either; this keeps the line short).</summary>
    public static string Compress(IEnumerable<int> ids)
    {
        var sorted = ids.Distinct().OrderBy(x => x).ToList();
        var parts = new List<string>();
        for (int i = 0; i < sorted.Count;)
        {
            int j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
            parts.Add(j > i + 1 ? $"{sorted[i]}-{sorted[j]}" : j == i + 1 ? $"{sorted[i]},{sorted[j]}" : sorted[i].ToString());
            i = j + 1;
        }
        return string.Join(",", parts);
    }
}
