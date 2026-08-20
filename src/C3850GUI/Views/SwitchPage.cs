using System.Windows;
using System.Windows.Controls;
using C3850GUI.Services;
using Wpf.Ui.Controls;

namespace C3850GUI.Views;

/// <summary>
/// Base page: exposes the active session, refreshes when the active switch changes,
/// and wraps command execution with error toasts.
/// </summary>
public abstract class SwitchPage : Page
{
    protected SwitchPage()
    {
        App.Sessions.ActiveChanged += OnActiveChangedInternal;
        Loaded += async (_, _) => { if (!_loadedOnce || _dirty) { _loadedOnce = true; _dirty = false; if (Connected) await SafeRefreshAsync(); } };
    }

    private bool _loadedOnce, _dirty;

    protected SshSession? Session => App.Sessions.Active;
    protected bool Connected => Session?.IsConnected == true;
    protected static MainWindow Main => MainWindow.Instance!;

    private async void OnActiveChangedInternal()
    {
        if (IsLoaded && Connected) await SafeRefreshAsync();
        else _dirty = true;
        OnActiveChanged();
    }

    protected virtual void OnActiveChanged() { }

    /// <summary>Reload this page's data from the switch. Override in pages that show live data.</summary>
    protected virtual Task RefreshAsync() => Task.CompletedTask;

    protected async Task SafeRefreshAsync()
    {
        try { await RefreshAsync(); }
        catch (Exception ex) { Toast(ex.Message, ControlAppearance.Danger); }
    }

    protected bool RequireConnection()
    {
        if (Connected) return true;
        Toast("Not connected. Pick a switch in the title bar and press Connect.", ControlAppearance.Caution);
        return false;
    }

    protected async Task<CommandResult?> RunAsync(string command, TimeSpan? timeout = null)
    {
        if (!RequireConnection()) return null;
        try { return await Session!.RunAsync(command, default, timeout); }
        catch (Exception ex) { Toast($"{command}: {ex.Message}", ControlAppearance.Danger); return null; }
    }

    /// <summary>Run config lines with optional confirmation (per Settings), toast the outcome.</summary>
    protected async Task<bool> ConfigureAsync(string what, params string[] lines)
    {
        if (!RequireConnection()) return false;
        if (App.Store.Settings.ConfirmConfigCommands &&
            !Dialogs.Confirm(this, what, "The following will be sent in configure terminal:\n\n" + string.Join("\n", lines)))
            return false;
        try
        {
            var r = await Session!.ConfigureAsync(lines);
            if (r.Error) { Toast($"{what}: {r.ErrorText}", ControlAppearance.Danger, 8); return false; }
            Toast($"{what} — done", ControlAppearance.Success);
            return true;
        }
        catch (Exception ex) { Toast($"{what}: {ex.Message}", ControlAppearance.Danger); return false; }
    }

    protected static void Toast(string msg, ControlAppearance a = ControlAppearance.Secondary, int seconds = 4) => Main.Toast(msg, a, seconds);
}
