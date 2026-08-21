#if DEBUG
using System.Windows;
using C3850GUI.Models;
namespace C3850GUI.Views;
public static class DialogPreview
{
    public static void Run()
    {
        if (Environment.GetCommandLineArgs().Contains("--load-all-pages"))
        {
            // construct every page so XAML/ctor errors show up without a switch
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var errors = new List<string>();
                foreach (var t in typeof(DialogPreview).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(SwitchPage)) && !t.IsAbstract))
                    try { Activator.CreateInstance(t); } catch (Exception ex) { errors.Add($"{t.Name}: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c3850-pages.txt"), errors.Count == 0 ? "ALL OK" : string.Join(Environment.NewLine, errors));
                Application.Current.Shutdown();
            });
        }
        if (Environment.GetCommandLineArgs().Contains("--preview-access"))
        {
            var vlans = new List<VlanInfo> { new() { Id = 1, Name = "default" }, new() { Id = 10, Name = "Servers" }, new() { Id = 20, Name = "Workstations" }, new() { Id = 50, Name = "VoIP" } };
            Application.Current.Dispatcher.BeginInvoke(() => TrunkDialog.ShowAccess(Application.Current.MainWindow, vlans, 20, 50, 3));
        }
        if (Environment.GetCommandLineArgs().Contains("--preview-trunk"))
        {
            var vlans = new List<VlanInfo> { new() { Id = 1, Name = "default" }, new() { Id = 10, Name = "Servers" }, new() { Id = 20, Name = "Workstations" }, new() { Id = 50, Name = "VoIP" }, new() { Id = 60, Name = "Cameras" }, new() { Id = 99, Name = "Mgmt" } };
            Application.Current.Dispatcher.BeginInvoke(() => TrunkDialog.Show(Application.Current.MainWindow, "Trunk / VLANs", vlans, "te2/0/6-7", false, "1,10,50,60", 10));
        }
    }
}
#endif
