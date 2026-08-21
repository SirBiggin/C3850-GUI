using System.Collections.ObjectModel;
using System.Windows;
using C3850GUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace C3850GUI.Services;

public class ActivityEntry
{
    public DateTime Time { get; init; } = DateTime.Now;
    public string Switch { get; init; } = "";
    public string Command { get; init; } = "";
    public string Output { get; init; } = "";
    public bool Error { get; init; }
    public string TimeText => Time.ToString("HH:mm:ss");
    public string Summary => Output.Length > 140 ? Output[..140].Replace('\n', ' ') + "…" : Output.Replace('\n', ' ');
}

/// <summary>
/// Holds every open switch session (one per profile) and which one the UI is currently showing.
/// Everything UI-facing is marshalled to the dispatcher.
/// </summary>
public partial class SessionManager : ObservableObject
{
    private readonly Dictionary<Guid, SshSession> _sessions = new();

    [ObservableProperty] private SshSession? _active;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _statusText = "Not connected";

    public ObservableCollection<SshSession> Open { get; } = new();
    public ObservableCollection<ActivityEntry> Activity { get; } = new();

    public event Action? ActiveChanged;
    public event Action<SshSession, string>? SessionLost;

    public bool IsConnected => Active?.IsConnected == true;

    public SshSession? Get(SwitchProfile p) => _sessions.GetValueOrDefault(p.Id);

    public async Task<SshSession> ConnectAsync(SwitchProfile profile, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(profile.Id, out var existing) && existing.IsConnected)
        {
            Active = existing;
            return existing;
        }
        var s = new SshSession(profile);
        s.BusyChanged += b => Ui(() => { if (s == Active) Busy = b; });
        s.CommandExecuted += r => Ui(() =>
        {
            Activity.Insert(0, new ActivityEntry { Switch = profile.Name, Command = r.Command, Output = r.Output, Error = r.Error });
            while (Activity.Count > 500) Activity.RemoveAt(Activity.Count - 1);
        });
        s.Disconnected += msg => Ui(() =>
        {
            _sessions.Remove(profile.Id);
            Open.Remove(s);
            if (Active == s) { Active = Open.FirstOrDefault(); StatusText = $"Disconnected: {msg}"; }
            SessionLost?.Invoke(s, msg);
        });
        StatusText = $"Connecting to {profile.Endpoint}…";
        try
        {
            await s.ConnectAsync(ct);
        }
        catch
        {
            s.Dispose();
            StatusText = "Connection failed";
            throw;
        }
        _sessions[profile.Id] = s;
        Open.Add(s);
        Active = s;
        return s;
    }

    public void Disconnect(SshSession s)
    {
        _sessions.Remove(s.Profile.Id);
        Open.Remove(s);
        s.Disconnect();
        if (Active == s) Active = Open.FirstOrDefault();
    }

    public void DisconnectAll()
    {
        foreach (var s in Open.ToList()) Disconnect(s);
    }

    partial void OnActiveChanged(SshSession? value)
    {
        StatusText = value == null ? "Not connected" : $"{value.Hostname}  ·  {value.Profile.Endpoint}";
        Busy = value?.Busy ?? false;
        OnPropertyChanged(nameof(IsConnected));
        ActiveChanged?.Invoke();
    }

    private static void Ui(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) a(); else d.BeginInvoke(a);
    }
}
