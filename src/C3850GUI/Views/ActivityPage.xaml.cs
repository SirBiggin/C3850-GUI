using System.Windows;
using System.Windows.Controls;
using C3850GUI.Services;

namespace C3850GUI.Views;

public partial class ActivityPage : SwitchPage
{
    public ActivityPage()
    {
        InitializeComponent();
        Grid.ItemsSource = App.Sessions.Activity;
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is ActivityEntry a) Detail.Text = $"{a.Switch}  {a.Time:HH:mm:ss}\n> {a.Command}\n\n{a.Output}";
    }

    private void Clear_Click(object s, RoutedEventArgs e) { App.Sessions.Activity.Clear(); Detail.Clear(); }
}
