using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using C3850GUI.Models;
using C3850GUI.Services;
using C3850GUI.Views;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace C3850GUI;

/// <summary>Keeps one instance of each page alive so the terminal/explorer keep their state while navigating.</summary>
public class CachedPageProvider : INavigationViewPageProvider
{
    private readonly Dictionary<Type, object> _cache = new();
    public object? GetPage(Type pageType)
    {
        if (_cache.TryGetValue(pageType, out var p)) return p;
        try { p = Activator.CreateInstance(pageType)!; }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
        {
            // surface the real XAML/ctor error instead of "Exception has been thrown by the target of an invocation"
            throw new InvalidOperationException($"{pageType.Name} failed to load: {ex.InnerException.Message}", ex.InnerException);
        }
        _cache[pageType] = p;
        return p;
    }
}

public partial class MainWindow : FluentWindow
{
    public static MainWindow? Instance { get; private set; }
    public SnackbarService Snackbar { get; } = new();
    private bool _suppressPick;

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();
        Nav.SetPageProviderService(new CachedPageProvider());
        Snackbar.SetSnackbarPresenter(SnackbarHost);

        ProfilePicker.ItemsSource = App.Store.Profiles;
        App.Store.Profiles.CollectionChanged += (_, _) => RefreshPicker();
        App.Sessions.PropertyChanged += Sessions_PropertyChanged;
        App.Sessions.SessionLost += (s, msg) => Toast($"Lost connection to {s.Profile.Name}: {msg}", ControlAppearance.Danger);

        Loaded += (_, _) =>
        {
            RefreshPicker();
            Nav.Navigate(App.Store.Profiles.Count == 0 ? typeof(ProfilesPage) : typeof(DashboardPage));
        };
        Closing += OnClosing;
    }

    private void RefreshPicker()
    {
        _suppressPick = true;
        var last = App.Store.Settings.LastProfileId;
        ProfilePicker.SelectedItem = App.Store.Profiles.FirstOrDefault(p => p.Id == last) ?? App.Store.Profiles.FirstOrDefault();
        _suppressPick = false;
        UpdateConnectButton();
    }

    private void Sessions_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionManager.Busy):
                BusyRing.Visibility = App.Sessions.Busy ? Visibility.Visible : Visibility.Collapsed; break;
            case nameof(SessionManager.StatusText):
                StatusText.Text = App.Sessions.StatusText; break;
            case nameof(SessionManager.Active):
                if (App.Sessions.Active != null && ProfilePicker.SelectedItem != App.Sessions.Active.Profile)
                { _suppressPick = true; ProfilePicker.SelectedItem = App.Sessions.Active.Profile; _suppressPick = false; }
                UpdateConnectButton(); break;
        }
    }

    private SwitchProfile? Picked => ProfilePicker.SelectedItem as SwitchProfile;

    private void UpdateConnectButton()
    {
        var p = Picked;
        var connected = p != null && App.Sessions.Get(p)?.IsConnected == true;
        ConnectButton.Content = connected ? "Disconnect" : "Connect";
        ConnectButton.Appearance = connected ? ControlAppearance.Secondary : ControlAppearance.Primary;
        ConnectButton.Icon = new SymbolIcon(connected ? SymbolRegular.PlugDisconnected24 : SymbolRegular.PlugConnected24);
        ConnectButton.IsEnabled = p != null;
    }

    private async void ProfilePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPick || Picked == null) return;
        App.Store.Settings.LastProfileId = Picked.Id;
        var s = App.Sessions.Get(Picked);
        if (s?.IsConnected == true) App.Sessions.Active = s;
        UpdateConnectButton();
        if (s == null) await ConnectAsync(Picked);
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (Picked == null) return;
        var s = App.Sessions.Get(Picked);
        if (s?.IsConnected == true) { App.Sessions.Disconnect(s); UpdateConnectButton(); return; }
        await ConnectAsync(Picked);
    }

    public async Task ConnectAsync(SwitchProfile p)
    {
        ConnectButton.IsEnabled = false;
        BusyRing.Visibility = Visibility.Visible;
        try
        {
            await App.Sessions.ConnectAsync(p);
            Toast($"Connected to {App.Sessions.Active?.Hostname} ({p.Endpoint})", ControlAppearance.Success);
        }
        catch (Exception ex)
        {
            Toast($"Connect failed: {ex.Message}", ControlAppearance.Danger, 8);
        }
        finally
        {
            BusyRing.Visibility = App.Sessions.Busy ? Visibility.Visible : Visibility.Collapsed;
            UpdateConnectButton();
        }
    }

    public void Toast(string message, ControlAppearance appearance = ControlAppearance.Secondary, int seconds = 4)
    {
        Dispatcher.BeginInvoke(() => Snackbar.Show("C3850 GUI", message, appearance,
            new SymbolIcon(appearance switch
            {
                ControlAppearance.Danger => SymbolRegular.ErrorCircle24,
                ControlAppearance.Success => SymbolRegular.CheckmarkCircle24,
                ControlAppearance.Caution => SymbolRegular.Warning24,
                _ => SymbolRegular.Info24
            }), TimeSpan.FromSeconds(seconds)));
    }

    public void NavigateTo(Type page) => Nav.Navigate(page);

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        App.Store.Save();
    }
}
