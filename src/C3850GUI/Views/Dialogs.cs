using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBox = Wpf.Ui.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;

namespace C3850GUI.Views;

/// <summary>Small modal dialogs (confirm / prompt / multi-field) styled as Fluent windows.</summary>
public static class Dialogs
{
    private static FluentWindow MakeWindow(DependencyObject owner, string title, UIElement body, out Button ok, out Button cancel, double width = 460)
    {
        var okBtn = new Button { Content = "OK", Appearance = ControlAppearance.Primary, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancelBtn = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(okBtn); buttons.Children.Add(cancelBtn);
        var root = new StackPanel { Margin = new Thickness(22, 14, 22, 18) };
        root.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(body);
        root.Children.Add(buttons);
        var w = new FluentWindow
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ExtendsContentIntoTitleBar = true,
            WindowBackdropType = WindowBackdropType.Mica, Content = root, ShowInTaskbar = false
        };
        w.Owner = Window.GetWindow(owner) ?? Application.Current.MainWindow;
        ok = okBtn; cancel = cancelBtn;
        okBtn.Click += (_, _) => { w.DialogResult = true; w.Close(); };
        cancelBtn.Click += (_, _) => { w.DialogResult = false; w.Close(); };
        return w;
    }

    public static bool Confirm(DependencyObject owner, string title, string message, string okText = "Proceed", bool danger = false)
    {
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5 };
        var w = MakeWindow(owner, title, new ScrollViewer { Content = body, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, out var ok, out _);
        ok.Content = okText;
        if (danger) ok.Appearance = ControlAppearance.Danger;
        return w.ShowDialog() == true;
    }

    public static string? Prompt(DependencyObject owner, string title, string label, string initial = "", string placeholder = "")
    {
        var tb = new TextBox { Text = initial, PlaceholderText = placeholder, Margin = new Thickness(0, 4, 0, 0) };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = label });
        body.Children.Add(tb);
        var w = MakeWindow(owner, title, body, out _, out _);
        w.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return w.ShowDialog() == true ? tb.Text : null;
    }

    /// <summary>Prompt for several labelled values at once. Returns null on cancel.</summary>
    public static Dictionary<string, string>? Form(DependencyObject owner, string title, params (string label, string initial, string placeholder)[] fields)
    {
        var boxes = new Dictionary<string, TextBox>();
        var body = new StackPanel();
        foreach (var (label, initial, ph) in fields)
        {
            body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 0) });
            var tb = new TextBox { Text = initial, PlaceholderText = ph, Margin = new Thickness(0, 4, 0, 0) };
            boxes[label] = tb;
            body.Children.Add(tb);
        }
        var w = MakeWindow(owner, title, body, out _, out _);
        w.Loaded += (_, _) => boxes.Values.FirstOrDefault()?.Focus();
        return w.ShowDialog() == true ? boxes.ToDictionary(k => k.Key, v => v.Value.Text) : null;
    }

    public static void ShowText(DependencyObject owner, string title, string text)
    {
        var tb = new System.Windows.Controls.TextBox
        {
            Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 300, MaxHeight = 600
        };
        var w = MakeWindow(owner, title, tb, out var ok, out var cancel, 820);
        cancel.Visibility = Visibility.Collapsed; ok.Content = "Close";
        w.ResizeMode = ResizeMode.CanResize;
        w.ShowDialog();
    }
}
