using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using C3850GUI.Models;

namespace C3850GUI.Services;

public class AppSettings
{
    public List<SwitchProfile> Profiles { get; set; } = new();
    public Guid? LastProfileId { get; set; }
    public string Theme { get; set; } = "Dark";
    public string TerminalFont { get; set; } = "Cascadia Mono";
    public double TerminalFontSize { get; set; } = 13;
    public int RefreshSeconds { get; set; } = 30;
    public bool ConfirmConfigCommands { get; set; } = true;
}

public class ProfileStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "C3850GUI");
    public static string FilePath { get; } = Path.Combine(Dir, "settings.json");
    public static string BackupDir { get; } = Path.Combine(Dir, "backups");

    public AppSettings Settings { get; private set; } = new();
    public ObservableCollection<SwitchProfile> Profiles { get; } = new();

    public void Load()
    {
        Directory.CreateDirectory(Dir);
        Directory.CreateDirectory(BackupDir);
        if (File.Exists(FilePath))
        {
            try { Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new(); }
            catch { Settings = new(); }
        }
        Profiles.Clear();
        foreach (var p in Settings.Profiles) Profiles.Add(p);
    }

    public void Save()
    {
        Settings.Profiles = Profiles.ToList();
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, Json));
    }
}
