using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace C3850GUI.Controls;

/// <summary>
/// A small, fast terminal surface tuned for IOS CLI sessions: handles CR/LF/BS, erase-to-EOL,
/// cursor-left sequences, strips other ANSI, keeps a scrollback, supports selection/copy/paste
/// and forwards keystrokes (including tab-completion and '?') to the session.
/// </summary>
public class TerminalControl : Control
{
    private readonly List<StringBuilder> _lines = new() { new StringBuilder() };
    private int _row, _col;
    private int _scrollOffset;                 // lines scrolled up from the bottom
    private const int MaxScrollback = 8000;
    private readonly StringBuilder _pending = new();
    private readonly object _pendingLock = new();
    private bool _renderQueued;
    private double _charW = 8, _charH = 16;
    private Typeface _typeface = new("Cascadia Mono");
    private (int r, int c)? _selStart, _selEnd;
    private bool _selecting;
    private readonly DispatcherTimer _caretTimer;
    private bool _caretOn = true;
    private bool _inEscape; private readonly StringBuilder _esc = new();

    public static readonly DependencyProperty FontFamilyNameProperty = DependencyProperty.Register(nameof(FontFamilyName), typeof(string), typeof(TerminalControl), new PropertyMetadata("Cascadia Mono", (d, _) => ((TerminalControl)d).Remeasure()));
    public static readonly DependencyProperty TerminalFontSizeProperty = DependencyProperty.Register(nameof(TerminalFontSize), typeof(double), typeof(TerminalControl), new PropertyMetadata(13.0, (d, _) => ((TerminalControl)d).Remeasure()));
    public static readonly DependencyProperty ForegroundBrushProperty = DependencyProperty.Register(nameof(ForegroundBrush), typeof(Brush), typeof(TerminalControl), new PropertyMetadata(Brushes.Gainsboro, (d, _) => ((TerminalControl)d).InvalidateVisual()));
    public static readonly DependencyProperty BackgroundBrushProperty = DependencyProperty.Register(nameof(BackgroundBrush), typeof(Brush), typeof(TerminalControl), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x14)), (d, _) => ((TerminalControl)d).InvalidateVisual()));
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(TerminalControl), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0xFF))));

    public string FontFamilyName { get => (string)GetValue(FontFamilyNameProperty); set => SetValue(FontFamilyNameProperty, value); }
    public double TerminalFontSize { get => (double)GetValue(TerminalFontSizeProperty); set => SetValue(TerminalFontSizeProperty, value); }
    public Brush ForegroundBrush { get => (Brush)GetValue(ForegroundBrushProperty); set => SetValue(ForegroundBrushProperty, value); }
    public Brush BackgroundBrush { get => (Brush)GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    /// <summary>Raised with the bytes (as string) the user typed; wire to SshSession.Write.</summary>
    public event Action<string>? Input;

    public TerminalControl()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.IBeam;
        SnapsToDevicePixels = true;
        _caretTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(530), DispatcherPriority.Background, (_, _) => { _caretOn = !_caretOn; InvalidateVisual(); }, Dispatcher);
        _caretTimer.Start();
        Remeasure();
    }

    private void Remeasure()
    {
        _typeface = new Typeface(new FontFamily(FontFamilyName + ", Consolas, Courier New"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var ft = MakeText("M");
        _charW = Math.Max(1, ft.WidthIncludingTrailingWhitespace);
        _charH = Math.Max(1, ft.Height);
        InvalidateVisual();
    }

    private FormattedText MakeText(string s, Brush? b = null) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, TerminalFontSize, b ?? ForegroundBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // ----------------------------------------------------------------- feeding data

    /// <summary>Thread-safe: queue incoming text; it is applied on the UI thread in batches.</summary>
    public void Feed(string text)
    {
        lock (_pendingLock) _pending.Append(text);
        if (_renderQueued) return;
        _renderQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            string chunk;
            lock (_pendingLock) { chunk = _pending.ToString(); _pending.Clear(); _renderQueued = false; }
            Apply(chunk);
            _scrollOffset = 0;
            InvalidateVisual();
        });
    }

    public void Clear()
    {
        _lines.Clear(); _lines.Add(new StringBuilder()); _row = _col = 0; _scrollOffset = 0; ClearSelection(); InvalidateVisual();
    }

    public void WriteLocal(string text, bool newline = true) { Apply(text + (newline ? "\r\n" : "")); InvalidateVisual(); }

    private void Apply(string s)
    {
        foreach (var ch in s)
        {
            if (_inEscape)
            {
                _esc.Append(ch);
                if (_esc.Length == 1 && ch != '[' && ch != ']' && ch != '(' && ch != ')') { _inEscape = false; _esc.Clear(); continue; }
                if (_esc.Length >= 2 && ch >= '@' && ch <= '~') { HandleCsi(_esc.ToString()); _inEscape = false; _esc.Clear(); }
                else if (_esc.Length > 32) { _inEscape = false; _esc.Clear(); }
                continue;
            }
            switch (ch)
            {
                case '\x1B': _inEscape = true; _esc.Clear(); break;
                case '\r': _col = 0; break;
                case '\n': NewLine(); break;
                case '\b': if (_col > 0) _col--; break;
                case '\a': break;
                case '\t': Put(' '); while (_col % 8 != 0) Put(' '); break;
                default:
                    if (ch >= ' ' || ch > '\x7F') Put(ch);
                    break;
            }
        }
    }

    private void HandleCsi(string seq)
    {
        // seq like "[K", "[2K", "[3D", "[0m"
        if (seq.Length < 2 || seq[0] != '[') return;
        var fin = seq[^1];
        var arg = seq[1..^1];
        int n = int.TryParse(arg.Split(';')[0], out var v) ? v : 0;
        var line = _lines[_row];
        switch (fin)
        {
            case 'K':
                if (n == 0 && _col < line.Length) line.Length = _col;
                else if (n == 2) { line.Clear(); }
                else if (n == 1) { for (int i = 0; i < Math.Min(_col, line.Length); i++) line[i] = ' '; }
                break;
            case 'D': _col = Math.Max(0, _col - Math.Max(1, n)); break;
            case 'C': _col += Math.Max(1, n); break;
            case 'J': if (n == 2) Clear(); break;
            case 'H': case 'f': if (arg == "" || arg == "1;1") { /* home: treat as new screen */ NewLine(); } break;
            // colours / modes ignored
        }
    }

    private void Put(char ch)
    {
        var line = _lines[_row];
        while (line.Length < _col) line.Append(' ');
        if (_col < line.Length) line[_col] = ch; else line.Append(ch);
        _col++;
    }

    private void NewLine()
    {
        _row++;
        if (_row >= _lines.Count) _lines.Add(new StringBuilder());
        if (_lines.Count > MaxScrollback) { _lines.RemoveRange(0, _lines.Count - MaxScrollback); _row = _lines.Count - 1; }
    }

    // ----------------------------------------------------------------- rendering

    private int VisibleRows => Math.Max(1, (int)(ActualHeight / _charH));

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        int rows = VisibleRows;
        int last = _lines.Count - 1 - _scrollOffset;
        int first = Math.Max(0, last - rows + 1);
        var selBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x2E, 0x8B, 0xFF));
        double y = ActualHeight - (last - first + 1) * _charH;
        if (y < 0) y = 0;
        var pad = 6.0;
        for (int r = first; r <= last; r++, y += _charH)
        {
            var text = _lines[r].ToString();
            if (TrySelectionRange(r, text.Length, out var a, out var b) && b > a)
                dc.DrawRectangle(selBrush, null, new Rect(pad + a * _charW, y, (b - a) * _charW, _charH));
            if (text.Length > 0) dc.DrawText(MakeText(text), new Point(pad, y));
            if (r == _row && _scrollOffset == 0 && _caretOn && IsFocused)
                dc.DrawRectangle(AccentBrush, null, new Rect(pad + _col * _charW, y + _charH - 2, _charW, 2));
        }
        if (_scrollOffset > 0)
        {
            var ft = MakeText($"↑ {_scrollOffset} lines", AccentBrush);
            dc.DrawText(ft, new Point(ActualWidth - ft.Width - 12, 6));
        }
    }

    // ----------------------------------------------------------------- selection

    private bool TrySelectionRange(int row, int len, out int a, out int b)
    {
        a = b = 0;
        if (_selStart is not { } s || _selEnd is not { } e) return false;
        var (s1, e1) = (s.r < e.r || (s.r == e.r && s.c <= e.c)) ? (s, e) : (e, s);
        if (row < s1.r || row > e1.r) return false;
        a = row == s1.r ? Math.Min(s1.c, len) : 0;
        b = row == e1.r ? Math.Min(e1.c, len) : len;
        return true;
    }

    private (int r, int c) HitTest(Point p)
    {
        int rows = VisibleRows;
        int last = _lines.Count - 1 - _scrollOffset;
        int first = Math.Max(0, last - rows + 1);
        double y0 = Math.Max(0, ActualHeight - (last - first + 1) * _charH);
        int r = first + (int)((p.Y - y0) / _charH);
        r = Math.Clamp(r, 0, _lines.Count - 1);
        int c = Math.Max(0, (int)Math.Round((p.X - 6) / _charW));
        return (r, c);
    }

    public string SelectedText
    {
        get
        {
            if (_selStart == null || _selEnd == null) return "";
            var sb = new StringBuilder();
            for (int r = 0; r < _lines.Count; r++)
            {
                var t = _lines[r].ToString();
                if (TrySelectionRange(r, t.Length, out var a, out var b)) { sb.Append(t, a, b - a); if (r != Math.Max(_selStart.Value.r, _selEnd.Value.r)) sb.AppendLine(); }
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>Entire scrollback as text.</summary>
    public string AllText => string.Join(Environment.NewLine, _lines.Select(l => l.ToString().TrimEnd()));

    public void ClearSelection() { _selStart = _selEnd = null; InvalidateVisual(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        _selStart = _selEnd = HitTest(e.GetPosition(this));
        _selecting = true; CaptureMouse(); InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_selecting) { _selEnd = HitTest(e.GetPosition(this)); InvalidateVisual(); }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _selecting = false; ReleaseMouseCapture();
        if (_selStart == _selEnd) ClearSelection();
    }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        // Right-click: copy selection if any, otherwise paste (PuTTY style)
        if (!string.IsNullOrEmpty(SelectedText)) { Clipboard.SetText(SelectedText); ClearSelection(); }
        else Paste();
        e.Handled = true;
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        _scrollOffset = Math.Clamp(_scrollOffset + (e.Delta > 0 ? 3 : -3), 0, Math.Max(0, _lines.Count - 1));
        InvalidateVisual();
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { _caretOn = true; InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) => InvalidateVisual();

    // ----------------------------------------------------------------- keyboard

    private void Paste()
    {
        if (!Clipboard.ContainsText()) return;
        var t = Clipboard.GetText().Replace("\r\n", "\r").Replace('\n', '\r');
        Input?.Invoke(t);
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        if (e.Text == "\r" || e.Text == "\n") return; // handled in OnKeyDown
        Input?.Invoke(e.Text);
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        string? send = e.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\x7f",
            Key.Tab => "\t",
            Key.Escape => "\x1b",
            Key.Up => "\x1b[A",
            Key.Down => "\x1b[B",
            Key.Right => "\x1b[C",
            Key.Left => "\x1b[D",
            Key.Home => "\x01",
            Key.End => "\x05",
            Key.Delete => "\x04",
            _ => null
        };
        if (e.Key == Key.PageUp) { _scrollOffset = Math.Min(_lines.Count - 1, _scrollOffset + VisibleRows); InvalidateVisual(); e.Handled = true; return; }
        if (e.Key == Key.PageDown) { _scrollOffset = Math.Max(0, _scrollOffset - VisibleRows); InvalidateVisual(); e.Handled = true; return; }
        if (ctrl)
        {
            if (e.Key == Key.V || (e.Key == Key.Insert)) { Paste(); e.Handled = true; return; }
            if (e.Key == Key.C && (shift || !string.IsNullOrEmpty(SelectedText)))
            {
                if (!string.IsNullOrEmpty(SelectedText)) Clipboard.SetText(SelectedText);
                ClearSelection(); e.Handled = true; return;
            }
            if (e.Key == Key.L) { Clear(); e.Handled = true; return; }
            if (e.Key >= Key.A && e.Key <= Key.Z) { send = ((char)(e.Key - Key.A + 1)).ToString(); }
            else if (e.Key == Key.OemOpenBrackets) send = "\x1b";
        }
        if (send != null) { Input?.Invoke(send); e.Handled = true; }
    }
}
