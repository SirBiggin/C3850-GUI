#if DEBUG
using System.Windows;
using C3850GUI.Models;
namespace C3850GUI.Views;
public static class DialogPreview
{
    public static void Run()
    {
        if (Environment.GetCommandLineArgs().Contains("--preview-trunk"))
        {
            var vlans = new List<VlanInfo> { new() { Id = 1, Name = "default" }, new() { Id = 10, Name = "Servers" }, new() { Id = 20, Name = "Workstations" }, new() { Id = 50, Name = "VoIP" }, new() { Id = 60, Name = "Cameras" }, new() { Id = 99, Name = "Mgmt" } };
            Application.Current.Dispatcher.BeginInvoke(() => TrunkDialog.Show(Application.Current.MainWindow, "Trunk / VLANs", vlans, "te2/0/6-7", false, "1,10,50,60", 10));
        }
    }
}
#endif
