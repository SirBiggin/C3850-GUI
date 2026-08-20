using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using C3850GUI.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace C3850GUI.Services;

public record HelpEntry(string Token, string Help);

public class CommandResult
{
    public string Command { get; init; } = "";
    public string Output { get; init; } = "";
    public bool Error => Output.Contains("\n% ") || Output.StartsWith("% ");
    public string ErrorText => string.Join(" | ", Output.Split('\n').Where(l => l.TrimStart().StartsWith('%')).Select(l => l.Trim()));
}

/// <summary>
/// One interactive SSH shell to a switch. A single shell stream is shared by the terminal
/// view and programmatic commands; programmatic commands are serialized through a lock and
/// their output still flows to the terminal so the user can always see what the GUI did.
/// </summary>
public sealed class SshSession : IDisposable
{
    private SshClient? _client;
    private ShellStream? _shell;
    private Thread? _reader;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly StringBuilder _capture = new();
    private readonly object _captureLock = new();
    private volatile bool _capturing;
    private readonly CancellationTokenSource _cts = new();

    // Matches an IOS prompt at the end of the buffer: "Switch#", "Switch>", "Switch(config-if)#"
    private static readonly Regex PromptTail = new(@"(?:^|\n)(?<prompt>[^\r\n]{1,64}?(?:\([\w\-]+[^\)]*\))?[>#])[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex AnsiRe = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex HelpLine = new(@"^\s{1,4}(?<tok>\S+)(?:\s{2,}(?<help>.*))?$", RegexOptions.Compiled);

    public SwitchProfile Profile { get; }
    public bool IsConnected => _client?.IsConnected == true;
    public string Hostname { get; private set; } = "";
    public string LastPrompt { get; private set; } = "";
    public bool Busy => _lock.CurrentCount == 0;

    /// <summary>Raw text from the switch, for the terminal control.</summary>
    public event Action<string>? DataReceived;
    public event Action<string>? Disconnected;
    public event Action<CommandResult>? CommandExecuted;
    public event Action<bool>? BusyChanged;

    public SshSession(SwitchProfile profile) => Profile = profile;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var p = Profile;
        ConnectionInfo info;
        if (p.Auth == AuthMode.PrivateKey)
        {
            var pass = Protector.Unprotect(p.ProtectedKeyPassphrase);
            var key = string.IsNullOrEmpty(pass) ? new PrivateKeyFile(p.PrivateKeyPath) : new PrivateKeyFile(p.PrivateKeyPath, pass);
            info = new ConnectionInfo(p.Host, p.Port, p.Username, new PrivateKeyAuthenticationMethod(p.Username, key));
        }
        else
        {
            var pw = Protector.Unprotect(p.ProtectedPassword);
            // Some IOS builds only offer keyboard-interactive; support both.
            var kbd = new KeyboardInteractiveAuthenticationMethod(p.Username);
            kbd.AuthenticationPrompt += (_, e) => { foreach (var pr in e.Prompts) pr.Response = pw; };
            info = new ConnectionInfo(p.Host, p.Port, p.Username, new PasswordAuthenticationMethod(p.Username, pw), kbd);
        }
        info.Timeout = TimeSpan.FromSeconds(15);
        await ConnectCoreAsync(info, ct);
    }

    private async Task ConnectCoreAsync(ConnectionInfo info, CancellationToken ct)
    {
        _client = new SshClient(info);
        _client.HostKeyReceived += (_, e) => e.CanTrust = true;
        _client.KeepAliveInterval = TimeSpan.FromSeconds(30);
        _client.ErrorOccurred += (_, e) => Disconnected?.Invoke(e.Exception.Message);
        await _client.ConnectAsync(ct);

        var modes = new Dictionary<TerminalModes, uint> { { TerminalModes.ECHO, 1 } };
        _shell = _client.CreateShellStream("vt100", 250, 60, 1600, 900, 1 << 20, modes);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "ssh-reader" };
        _reader.Start();

        // Wait for first prompt, then make output non-paged and as wide as possible.
        await _lock.WaitAsync(ct);
        try
        {
            BusyChanged?.Invoke(true);
            BeginCapture();
            await WaitForPromptAsync(TimeSpan.FromSeconds(15), ct);
            if (LastPrompt.EndsWith('>')) await EnableAsync(ct);
            await SendAndWaitAsync("terminal length 0", ct);
            await SendAndWaitAsync("terminal width 0", ct);
            Hostname = LastPrompt.TrimEnd('#', '>');
        }
        finally { EndCapture(); _lock.Release(); BusyChanged?.Invoke(false); }
    }

    private async Task EnableAsync(CancellationToken ct)
    {
        var secret = Protector.Unprotect(Profile.ProtectedEnableSecret);
        if (string.IsNullOrEmpty(secret)) secret = Protector.Unprotect(Profile.ProtectedPassword);
        BeginCapture();
        Write("enable\n");
        var got = await WaitForAsync(s => s.TrimEnd().EndsWith("Password:") || PromptTail.IsMatch(s), TimeSpan.FromSeconds(10), ct);
        if (got.TrimEnd().EndsWith("Password:"))
        {
            BeginCapture();
            Write(secret + "\n");
            await WaitForPromptAsync(TimeSpan.FromSeconds(10), ct);
        }
        else LastPrompt = PromptTail.Match(got).Groups["prompt"].Value.Trim();
        if (!LastPrompt.EndsWith('#')) throw new InvalidOperationException("Could not enter privileged EXEC mode (enable failed).");
    }

    // ------------------------------------------------------------------ I/O plumbing

    private void ReadLoop()
    {
        var buf = new byte[16384];
        try
        {
            while (!_cts.IsCancellationRequested && _shell != null)
            {
                int n = _shell.Read(buf, 0, buf.Length);
                if (n <= 0) { if (_client?.IsConnected != true) break; Thread.Sleep(10); continue; }
                var text = Encoding.UTF8.GetString(buf, 0, n);
                if (_capturing) lock (_captureLock) _capture.Append(text);
                DataReceived?.Invoke(text);
            }
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested) { Disconnected?.Invoke(ex.Message); return; }
        if (!_cts.IsCancellationRequested) Disconnected?.Invoke("Connection closed.");
    }

    /// <summary>Raw write from the terminal view. Does not take the command lock.</summary>
    public void Write(string s)
    {
        if (_shell == null) return;
        var b = Encoding.UTF8.GetBytes(s);
        _shell.Write(b, 0, b.Length);
        _shell.Flush();
    }

    private void BeginCapture() { lock (_captureLock) _capture.Clear(); _capturing = true; }
    private void EndCapture() { _capturing = false; }
    private string Captured() { lock (_captureLock) return AnsiRe.Replace(_capture.ToString(), "").Replace("\r", ""); }

    private async Task<string> WaitForAsync(Func<string, bool> done, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var s = Captured();
            if (done(s)) return s;
            if (!IsConnected) throw new IOException("Disconnected.");
            await Task.Delay(25, ct);
        }
        throw new TimeoutException("Timed out waiting for switch prompt. Last output:\n" + Tail(Captured(), 400));
    }

    private async Task<string> WaitForPromptAsync(TimeSpan timeout, CancellationToken ct)
    {
        var s = await WaitForAsync(x => PromptTail.IsMatch(x), timeout, ct);
        LastPrompt = PromptTail.Match(s).Groups["prompt"].Value.Trim();
        return s;
    }

    private async Task<string> SendAndWaitAsync(string cmd, CancellationToken ct, TimeSpan? timeout = null)
    {
        BeginCapture();
        Write(cmd + "\n");
        var s = await WaitForPromptAsync(timeout ?? TimeSpan.FromSeconds(30), ct);
        EndCapture();
        return StripEchoAndPrompt(s, cmd);
    }

    private static string StripEchoAndPrompt(string s, string cmd)
    {
        var lines = s.Split('\n').ToList();
        if (lines.Count > 0 && PromptTail.IsMatch("\n" + lines[^1])) lines.RemoveAt(lines.Count - 1);
        var idx = lines.FindIndex(l => l.TrimEnd().EndsWith(cmd.Trim()));
        if (idx >= 0) lines.RemoveRange(0, idx + 1);
        return string.Join('\n', lines).Trim('\n');
    }

    private static string Tail(string s, int n) => s.Length <= n ? s : s[^n..];

    // ------------------------------------------------------------------ public API

    /// <summary>Run one EXEC-mode command and return its output.</summary>
    public async Task<CommandResult> RunAsync(string command, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        await _lock.WaitAsync(ct);
        try
        {
            BusyChanged?.Invoke(true);
            var outp = await SendAndWaitAsync(command, ct, timeout);
            var r = new CommandResult { Command = command, Output = outp };
            CommandExecuted?.Invoke(r);
            return r;
        }
        finally { EndCapture(); _lock.Release(); BusyChanged?.Invoke(false); }
    }

    /// <summary>
    /// Run a sequence of commands in global config mode, wrapped in "configure terminal" / "end".
    /// Output of all lines is concatenated; any "% ..." line marks an error.
    /// </summary>
    public async Task<CommandResult> ConfigureAsync(IEnumerable<string> lines, CancellationToken ct = default)
    {
        var list = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        await _lock.WaitAsync(ct);
        try
        {
            BusyChanged?.Invoke(true);
            var sb = new StringBuilder();
            sb.AppendLine(await SendAndWaitAsync("configure terminal", ct));
            foreach (var l in list)
            {
                var o = await SendAndWaitAsync(l, ct);
                if (o.Length > 0) sb.AppendLine(o);
            }
            sb.AppendLine(await SendAndWaitAsync("end", ct));
            var r = new CommandResult { Command = "conf t: " + string.Join("; ", list), Output = sb.ToString().Trim() };
            CommandExecuted?.Invoke(r);
            return r;
        }
        finally { EndCapture(); _lock.Release(); BusyChanged?.Invoke(false); }
    }

    /// <summary>
    /// Send lines in order exactly as given (no automatic conf t / end) and return the combined output.
    /// Used by the Command Explorer to run inside an arbitrary mode context.
    /// </summary>
    public async Task<CommandResult> RunSequenceAsync(IEnumerable<string> lines, CancellationToken ct = default)
    {
        var list = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        await _lock.WaitAsync(ct);
        try
        {
            BusyChanged?.Invoke(true);
            var sb = new StringBuilder();
            foreach (var l in list)
            {
                var o = await SendAndWaitAsync(l, ct);
                if (o.Length > 0) sb.AppendLine(o);
            }
            var r = new CommandResult { Command = string.Join("; ", list), Output = sb.ToString().Trim() };
            CommandExecuted?.Invoke(r);
            return r;
        }
        finally { EndCapture(); _lock.Release(); BusyChanged?.Invoke(false); }
    }

    /// <summary>
    /// Run a command that asks a yes/no or [confirm] question (reload, write erase, copy ...),
    /// answering each prompt with <paramref name="answer"/> until the EXEC prompt returns.
    /// </summary>
    public async Task<CommandResult> RunInteractiveAsync(string command, string answer = "", CancellationToken ct = default, TimeSpan? timeout = null)
    {
        await _lock.WaitAsync(ct);
        try
        {
            BusyChanged?.Invoke(true);
            BeginCapture();
            Write(command + "\n");
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(120));
            var full = new StringBuilder();
            while (true)
            {
                var s = await WaitForAsync(x =>
                {
                    var t = x.TrimEnd();
                    return PromptTail.IsMatch(x) || t.EndsWith("[confirm]") || t.EndsWith("]?") || t.EndsWith("]:") || t.EndsWith("?") || t.EndsWith(":");
                }, deadline - DateTime.UtcNow, ct);
                if (PromptTail.IsMatch(s)) { full.Append(s); LastPrompt = PromptTail.Match(s).Groups["prompt"].Value.Trim(); break; }
                full.Append(s);
                BeginCapture();
                Write(answer + "\n");
            }
            EndCapture();
            var r = new CommandResult { Command = command, Output = StripEchoAndPrompt(full.ToString(), command) };
            CommandExecuted?.Invoke(r);
            return r;
        }
        finally { EndCapture(); _lock.Release(); BusyChanged?.Invoke(false); }
    }

    /// <summary>
    /// Ask the IOS parser what can follow <paramref name="prefix"/> (sends "prefix ?"), optionally
    /// after entering a sub-mode via <paramref name="context"/> commands (e.g. "configure terminal",
    /// "interface Gi1/0/1"). Always returns to EXEC with "end" afterwards.
    /// </summary>
    public async Task<List<HelpEntry>> QueryHelpAsync(IReadOnlyList<string> context, string prefix, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            BusyChanged?.Invoke(true);
            foreach (var c in context) await SendAndWaitAsync(c, ct);
            try
            {
                BeginCapture();
                var typed = prefix.TrimStart(); // keep a trailing space (next-token help) vs none (complete partial word) exactly as the caller typed it
                Write(typed + "?");
                // IOS prints help then redraws the prompt with the partial line still typed.
                var s = await WaitForAsync(x =>
                {
                    if (!x.Contains('\n')) return false;
                    var last = x.Split('\n')[^1];
                    if (typed.Length == 0) return PromptTail.IsMatch("\n" + last.TrimEnd());
                    return last.EndsWith(typed) && PromptTail.IsMatch("\n" + last[..^typed.Length].TrimEnd());
                }, TimeSpan.FromSeconds(20), ct);
                EndCapture();
                Write("\x15"); // Ctrl-U clears the pending line
                await Task.Delay(80, ct);
                return ParseHelp(s, typed);
            }
            finally
            {
                if (context.Count > 0) await SendAndWaitAsync("end", ct);
            }
        }
        finally { EndCapture(); _lock.Release(); BusyChanged?.Invoke(false); }
    }

    private static List<HelpEntry> ParseHelp(string s, string typed)
    {
        var list = new List<HelpEntry>();
        var lines = s.Replace("\r", "").Split('\n');
        int start = Array.FindIndex(lines, l => l.TrimEnd().EndsWith("?"));
        for (int i = start + 1; i < lines.Length - 1; i++)
        {
            var l = lines[i];
            if (l.StartsWith("% ")) { list.Add(new HelpEntry("% error", l.Trim())); break; }
            var m = HelpLine.Match(l);
            if (!m.Success) continue;
            var tok = m.Groups["tok"].Value;
            var help = m.Groups["help"].Success ? m.Groups["help"].Value.Trim() : "";
            if (tok == "<cr>" && (help == "<cr>" || help == "")) help = "Press Enter to execute";
            list.Add(new HelpEntry(tok, help));
        }
        return list.GroupBy(e => e.Token).Select(g => g.First()).ToList();
    }

    public void Disconnect()
    {
        try { _cts.Cancel(); } catch { }
        try { _shell?.Dispose(); } catch { }
        try { _client?.Disconnect(); _client?.Dispose(); } catch { }
        _shell = null; _client = null;
    }

    public void Dispose() => Disconnect();
}
