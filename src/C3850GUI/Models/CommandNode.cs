using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace C3850GUI.Models;

/// <summary>
/// One token in the IOS parser tree, discovered live from the switch via '?'.
/// Children are loaded lazily when the node is expanded.
/// </summary>
public partial class CommandNode : ObservableObject
{
    public CommandNode(string token, string help, CommandNode? parent)
    {
        Token = token; Help = help; Parent = parent;
        // Leaf markers and argument placeholders can't be expanded further in a meaningful way,
        // but anything else gets a dummy child so the TreeView shows an expander.
        if (!IsTerminal) Children.Add(Placeholder);
    }

    public static readonly CommandNode Placeholder = new("…", "", null, true);
    private CommandNode(string token, string help, CommandNode? parent, bool _) { Token = token; Help = help; Parent = parent; }

    public string Token { get; }
    public string Help { get; }
    public CommandNode? Parent { get; }
    public ObservableCollection<CommandNode> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isLoading;

    public bool IsCr => Token == "<cr>";
    /// <summary>Arguments like WORD, LINE, &lt;1-4094&gt;, A.B.C.D — user must type a value.</summary>
    public bool IsArgument => !IsCr && (Token.StartsWith('<') || Token.All(c => char.IsUpper(c) || c == '.' || c == ':' || c == '/' || c == '-' || c == '_'));
    /// <summary>Arguments need a typed value before the parser can say what follows, so the tree stops there; use the '?' builder for deeper help.</summary>
    public bool IsTerminal => IsCr || IsArgument;

    /// <summary>Full command path from the root, e.g. "show interfaces status".</summary>
    public string Path
    {
        get
        {
            var parts = new List<string>();
            for (var n = this; n is { Parent: not null }; n = n.Parent) parts.Add(n.Token);
            parts.Reverse();
            return string.Join(' ', parts.Where(p => p != "<cr>"));
        }
    }

    public string Icon => IsCr ? "" : IsArgument ? "" : "";
}
