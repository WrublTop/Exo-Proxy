using ExoProxy.Core;
using ExoProxy.Data;

namespace ExoProxy.Presentation.Screens.Base.Sections;

public sealed class CommsSection : IBaseSection
{
    public string SectionId => SectionIds.Comms;
    public BaseSectionResponse Response { get; private set; } = new(BaseSectionRequest.Stay, null);

    private enum CommState { List, Reading, ChoosingReply, Transmitting }
    private CommState _state = CommState.List;

    private readonly CommsRepository  _repo;
    private readonly OperatorAccount  _account;
    private readonly OperatorProgress _progress;
    private readonly IAudioService    _audio;

    // ── inbox ─────────────────────────────────────────────────────────────────
    private List<CommsMessage> _inbox      = [];
    private int                _listIndex  = 0;
    private int                _listScroll = 0;

    // ── reading ───────────────────────────────────────────────────────────────
    private CommsMessage?      _rootMessage      = null;
    private List<CommsMessage> _threadChain      = [];
    private List<string>       _threadLines        = [];
    private List<string>       _previewThreadLines = [];
    private int                _scrollOffset     = 0;
    private List<ReplyOption>  _pendingOptions   = [];
    private string?            _pendingMessageId = null;
    private int                _lastRightInnerW  = 0;

    // ── reply ─────────────────────────────────────────────────────────────────
    private int _replyIndex = 0;

    // ── transmit animation ────────────────────────────────────────────────────
    private ReplyOption?   _chosenOption     = null;
    private string?        _chosenForMsgId   = null;
    private TimeSpan       _txStartTime;
    private int            _txElapsedMs      = 0;
    private int            _typingDurationMs = 4000;

    private const int FrameCount = 8;
    private const int FrameMs    = 150;   // 7 intervals × 150ms = 1050ms arrow animation
    private const int TxHoldMs   = 1400;  // total TX box duration
    private const int SentMs     = 1500;  // MESSAGE SENT box duration
    private const int Gap1Ms     = 300;   // pause before typing starts
    private const int Gap2Ms     = 500;   // pause before RX
    private const int RxHoldMs   = 1400;  // total RX box duration

    // ── blink / clock ─────────────────────────────────────────────────────────
    private BlinkState _blink;
    private TimeSpan   _now;         // last Update tick — input handlers read this

    // ── layout ───────────────────────────────────────────────────────────────
    private const int LeftW  = 44;
    private const int LeftI  = LeftW - 2;   // 42
    private const int Gap    = 2;

    public CommsSection(OperatorAccount account, CommsRepository repo, OperatorProgress progress, IAudioService audio)
    {
        _account  = account;
        _repo     = repo;
        _progress = progress;
        _audio    = audio;
        _inbox    = _repo.GetInbox(_progress.Sol);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    public void Update(GameTime time, InputEvent? input)
    {
        var now = time.Total;
        _now = now;
        _blink.Update(now);

        Response = new(BaseSectionRequest.Stay, null);

        if (_state == CommState.Transmitting)
        {
            TickTransmit(now);
            return;
        }

        if (input is null) return;
        var key = input.Value.Key;

        switch (_state)
        {
            case CommState.List:          HandleListKey(key);  break;
            case CommState.Reading:       HandleReadKey(key);  break;
            case CommState.ChoosingReply: HandleReplyKey(key); break;
        }
    }

    private void HandleListKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            Response = new(BaseSectionRequest.GoToHub, null);
            return;
        }
        if (key.Key == ConsoleKey.UpArrow   && _listIndex > 0)                { _listIndex--; return; }
        if (key.Key == ConsoleKey.DownArrow && _listIndex < _inbox.Count - 1) { _listIndex++; return; }
        if (key.Key == ConsoleKey.Enter     && _inbox.Count > 0)              { _audio.Play("comms.open_thread"); OpenMessage(_inbox[_listIndex]); }
    }

    private void HandleReadKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)    { _state = CommState.List;   return; }
        if (key.Key == ConsoleKey.UpArrow)   { if (_scrollOffset > 0) _scrollOffset--; return; }
        if (key.Key == ConsoleKey.DownArrow) { _scrollOffset++; return; }
        if (_pendingOptions.Count > 0 &&
            (key.Key == ConsoleKey.R || key.Key == ConsoleKey.Enter))
        {
            _replyIndex = 0;
            _state = CommState.ChoosingReply;
        }
    }

    private void HandleReplyKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)                                              { _state = CommState.Reading; return; }
        if (key.Key == ConsoleKey.UpArrow   && _replyIndex > 0)                        { _replyIndex--; return; }
        if (key.Key == ConsoleKey.DownArrow && _replyIndex < _pendingOptions.Count - 1) { _replyIndex++; return; }
        if (key.Key == ConsoleKey.Enter)                                               { BeginTransmit(_pendingOptions[_replyIndex]); return; }

        int num = key.KeyChar - '1';
        if (num >= 0 && num < _pendingOptions.Count) BeginTransmit(_pendingOptions[num]);
    }

    private void OpenMessage(CommsMessage root)
    {
        _rootMessage      = root;
        var (chain, opts, pendingId) = _repo.BuildThread(root.Id);
        _threadChain      = chain;
        _pendingOptions   = opts ?? [];
        _pendingMessageId = pendingId;
        _scrollOffset     = 0;
        _lastRightInnerW  = 0;
        _repo.MarkThreadRead(root.Id);
        _inbox = _repo.GetInbox(_progress.Sol);
        _state = CommState.Reading;
    }

    private void BeginTransmit(ReplyOption option)
    {
        _audio.Play("comms.send");
        _chosenOption   = option;
        _chosenForMsgId = _pendingMessageId;
        _txStartTime    = _now;
        _txElapsedMs    = 0;

        // Typing duration = response body length / (40 WPM × 2) in ms, clamped to [2s, 14s]
        var responseMsg   = string.IsNullOrEmpty(option.Unlocks) ? null : _repo.GetMessage(option.Unlocks);
        int bodyLen       = responseMsg?.Body.TrimEnd().Length ?? 80;
        float charsPerSec = 13.3f;  // 40 WPM × 2 at ~5 chars/word
        _typingDurationMs = (int)Math.Clamp(bodyLen / charsPerSec * 1000f, 2000f, 14000f);

        RebuildPreviewLines();
        _state = CommState.Transmitting;
    }

    private void TickTransmit(TimeSpan now)
    {
        _txElapsedMs = (int)(now - _txStartTime).TotalMilliseconds;
        int typingStart = TxHoldMs + SentMs + Gap1Ms;
        int rxStart     = typingStart + _typingDurationMs + Gap2Ms;
        int doneAt      = rxStart + RxHoldMs;
        if (_txElapsedMs >= doneAt) CommitReply();
    }

    private void CommitReply()
    {
        if (_chosenOption == null || _chosenForMsgId == null || _rootMessage == null) return;
        _repo.CommitReply(_chosenForMsgId, _chosenOption.Id, _chosenOption.Unlocks);
        _inbox = _repo.GetInbox(_progress.Sol);
        OpenMessage(_rootMessage);
        _scrollOffset = int.MaxValue;
    }

    // ── Render ───────────────────────────────────────────────────────────────

    public void Render(IRenderBuffer buffer)
    {
        int panelTop    = 1;
        int panelBottom = buffer.Height - 3;

        int rightX      = LeftW + Gap;
        int rightW      = buffer.Width - rightX;
        int rightInnerW = rightW - 2;

        if (rightInnerW < 20) return;

        if (_rootMessage != null && rightInnerW != _lastRightInnerW)
        {
            _threadLines     = BuildThreadLines(_threadChain, rightInnerW - 4);
            _lastRightInnerW = rightInnerW;
            if (_state == CommState.Transmitting) RebuildPreviewLines();
        }

        RenderInboxPanel(buffer, 0, panelTop, panelBottom);
        RenderRightPanel(buffer, rightX, rightW, rightInnerW, panelTop, panelBottom);
        RenderHints(buffer, buffer.Height - 2, buffer.Width / 2);
    }

    // ── inbox panel ───────────────────────────────────────────────────────────

    private void RenderInboxPanel(IRenderBuffer buffer, int x, int panelTop, int panelBottom)
    {
        int innerH = panelBottom - panelTop - 1;

        if (_listIndex < _listScroll)                    _listScroll = _listIndex;
        if (_listIndex >= _listScroll + innerH)          _listScroll = _listIndex - innerH + 1;
        _listScroll = Math.Max(0, _listScroll);

        // Top border: "┌─ COMMS ─────────────── INBOX (3) ─┐"
        string ltTitle = " COMMS ";
        string rtTitle = $" INBOX ({_inbox.Count}) ";
        Ui.WriteDualTitleBorder(buffer, x, panelTop, LeftW,
            ltTitle, rtTitle,
            ExoColors.ProksText, ExoColors.ProksPale,
            ExoColors.ProksBorder);

        buffer.WriteAt(x, panelBottom, "└" + new string('─', LeftI) + "┘", ExoColors.ProksBorder);

        for (int row = panelTop + 1; row < panelBottom; row++)
        {
            buffer.WriteAt(x,          row, "│", ExoColors.ProksBorder);
            buffer.WriteAt(x + LeftW - 1, row, "│", ExoColors.ProksBorder);
        }

        for (int i = 0; i < innerH; i++)
        {
            int msgIdx = _listScroll + i;
            if (msgIdx >= _inbox.Count) break;
            RenderInboxRow(buffer, x, panelTop + 1 + i, _inbox[msgIdx], msgIdx == _listIndex);
        }
    }

    private void RenderInboxRow(IRenderBuffer buffer, int panelX, int y,
                                 CommsMessage msg, bool selected)
    {
        // Inner area layout (cols panelX+1 … panelX+42):
        // +1        : cursor arrow
        // +2        : space
        // +3 …+13  : sender  (11 chars)
        // +14       : space
        // +15…+33  : subject (19 chars)
        // +34       : space
        // +35…+42  : SOL     (8 chars, left-padded)

        bool   unread  = _repo.HasUnreadInThread(msg.Id);
        bool   isSuirdc = msg.Sender == "suirdc";
        string arrow   = selected ? (_blink.Visible ? "►" : "▷") : " ";
        string sender  = Ui.Truncate(msg.SenderDisplay, 11).PadRight(11);
        string subject = Ui.Truncate(msg.Subject, 19).PadRight(19);
        string sol     = msg.Sol.PadLeft(8);

        string arrowColor = selected
            ? (_blink.Visible ? ExoColors.PhosphorText : ExoColors.PhosphorDim)
            : ExoColors.ProksDark;
        string rowColor = selected ? ExoColors.PhosphorText
            : (unread ? (isSuirdc ? ExoColors.ProksText : ExoColors.PhosphorBright)
                      : ExoColors.ProksDark);

        buffer.WriteAt(panelX + 1,  y, arrow,   arrowColor);
        buffer.WriteAt(panelX + 3,  y, sender,  rowColor);
        buffer.WriteAt(panelX + 15, y, subject, rowColor);
        buffer.WriteAt(panelX + 35, y, sol,     ExoColors.ProksDark);
    }

    // ── right panel ───────────────────────────────────────────────────────────

    private void RenderRightPanel(IRenderBuffer buffer, int x, int w, int innerW,
                                   int panelTop, int panelBottom)
    {
        // Top border with context-sensitive title
        if (_state == CommState.List || _rootMessage == null)
        {
            string ltTitle = $" OPERATOR: {_account.Login} ";
            string rtTitle = $" {_progress.SolDisplay} ";
            Ui.WriteDualTitleBorder(buffer, x, panelTop, w,
                ltTitle, rtTitle,
                ExoColors.ProksDark, ExoColors.SignalText,
                ExoColors.ProksBorder);
        }
        else
        {
            bool   isSuirdc  = _rootMessage.Sender == "suirdc";
            string senderTitle = $" {_rootMessage.SenderDisplay} ";
            string senderColor = isSuirdc ? ExoColors.ProksText : ExoColors.PhosphorText;
            Ui.WriteDualTitleBorder(buffer, x, panelTop, w,
                senderTitle, "",
                senderColor, ExoColors.ProksBorder,
                ExoColors.ProksBorder);
        }

        buffer.WriteAt(x, panelBottom, "└" + new string('─', innerW) + "┘", ExoColors.ProksBorder);
        for (int row = panelTop + 1; row < panelBottom; row++)
        {
            buffer.WriteAt(x,         row, "│", ExoColors.ProksBorder);
            buffer.WriteAt(x + w - 1, row, "│", ExoColors.ProksBorder);
        }

        if (_state == CommState.List || _rootMessage == null)
        {
            RenderEmptyRight(buffer, x + 1, panelTop, panelBottom, innerW);
            return;
        }

        // Sub-header: empty + Subject+SOL + empty + separator  (4 rows)
        const int HeaderRows = 4;
        int contentX         = x + 1;
        RenderSubHeader(buffer, x, contentX, panelTop + 1, innerW, _rootMessage);

        int contentTop = panelTop + 1 + HeaderRows;

        int footerRows = 0;
        if (_pendingOptions.Count > 0 && _state != CommState.Transmitting)
            footerRows = _state == CommState.ChoosingReply
                ? 1 + _pendingOptions.Count
                : 2;

        int contentBottom = panelBottom - 1 - footerRows;
        int visibleRows   = Math.Max(0, contentBottom - contentTop + 1);

        switch (_state)
        {
            case CommState.Reading:
                RenderScrollContent(buffer, contentX, contentTop, visibleRows, innerW, _threadLines);
                if (_pendingOptions.Count > 0)
                    RenderReadingFooter(buffer, x, contentX, contentBottom + 1, contentBottom + 2, innerW);
                break;

            case CommState.ChoosingReply:
                RenderScrollContent(buffer, contentX, contentTop, visibleRows, innerW, _threadLines);
                RenderReplyOptions(buffer, x, contentX, contentBottom + 1, contentBottom + 2, innerW);
                break;

            case CommState.Transmitting:
                RenderTransmitContent(buffer, x, contentX, contentTop, visibleRows, innerW);
                break;
        }
    }

    private void RenderEmptyRight(IRenderBuffer buffer, int x, int panelTop, int panelBottom, int innerW)
    {
        int midY = (panelTop + panelBottom) / 2 - 1;
        const string line1 = "NO TRANSMISSION SELECTED";
        const string line2 = "↑↓  browse inbox     ENTER  open";
        buffer.WriteAt(x + (innerW - line1.Length) / 2, midY,     line1, ExoColors.ProksDark);
        buffer.WriteAt(x + (innerW - line2.Length) / 2, midY + 2, line2, ExoColors.ProksDark);
    }

    private void RenderSubHeader(IRenderBuffer buffer, int borderX, int x, int y,
                                  int innerW, CommsMessage msg)
    {
        bool   isSuirdc    = msg.Sender == "suirdc";
        string subjectColor = isSuirdc ? ExoColors.ProksText : ExoColors.PhosphorText;

        // row 0: empty
        // row 1: subject (left) + sol (right-aligned)
        string subject = Ui.Truncate(msg.Subject, innerW - 12);
        string sol     = msg.Sol;
        int    solX    = x + innerW - 2 - sol.Length;
        buffer.WriteAt(x + 2,  y + 1, subject, subjectColor);
        buffer.WriteAt(solX,   y + 1, sol,     ExoColors.ProksDark);
        // row 2: empty
        // row 3: separator
        buffer.WriteAt(borderX, y + 3, "├" + new string('─', innerW) + "┤", ExoColors.ProksBorder);
    }

    // ── scrollable content ────────────────────────────────────────────────────

    private void RenderScrollContent(IRenderBuffer buffer, int x, int top,
                                      int visibleRows, int innerW, List<string> lines)
    {
        if (visibleRows <= 0) return;

        int contentRows = visibleRows - 2;
        int maxScroll   = Math.Max(0, lines.Count - contentRows);
        _scrollOffset   = Math.Clamp(_scrollOffset, 0, maxScroll);

        bool hasAbove = _scrollOffset > 0;
        bool hasBelow = _scrollOffset + contentRows < lines.Count;

        if (hasAbove)
            buffer.WriteAt(x + (innerW - 2) / 2, top, "▲", ExoColors.ProksBorder);

        for (int i = 0; i < contentRows; i++)
        {
            int li = _scrollOffset + i;
            if (li >= lines.Count) break;
            var (text, color) = ParseLine(lines[li], innerW);
            buffer.WriteAt(x, top + 1 + i, text, color);
        }

        if (hasBelow)
            buffer.WriteAt(x + (innerW - 2) / 2, top + visibleRows - 1, "▼", ExoColors.ProksBorder);
    }

    // ── footer variants ───────────────────────────────────────────────────────

    private void RenderReadingFooter(IRenderBuffer buffer, int borderX, int x,
                                      int sepY, int footerY, int innerW)
    {
        buffer.WriteAt(borderX, sepY, "├" + new string('─', innerW) + "┤", ExoColors.ProksBorder);
        string label = "  [ R ]  Reply";
        buffer.WriteAt(x, footerY, label.PadRight(innerW), ExoColors.ProksText);
    }

    private void RenderReplyOptions(IRenderBuffer buffer, int borderX, int x,
                                     int sepY, int firstOptY, int innerW)
    {
        const string SepHeader = "─── REPLY ";
        buffer.WriteAt(borderX, sepY,
            "├" + SepHeader + new string('─', innerW - SepHeader.Length) + "┤",
            ExoColors.ProksBorder);

        for (int i = 0; i < _pendingOptions.Count; i++)
        {
            bool   sel        = i == _replyIndex;
            string arrow      = sel ? (_blink.Visible ? "►" : "▷") : " ";
            string arrowColor = sel
                ? (_blink.Visible ? ExoColors.PhosphorText : ExoColors.PhosphorDim)
                : ExoColors.ProksDark;
            string num        = $" {i + 1}  ";
            string text       = Ui.Truncate(_pendingOptions[i].Text, innerW - num.Length - 2);

            buffer.WriteAt(x,                  firstOptY + i, arrow, arrowColor);
            buffer.WriteAt(x + 1,              firstOptY + i, num,   ExoColors.ProksDark);
            buffer.WriteAt(x + 1 + num.Length, firstOptY + i,
                text.PadRight(innerW - num.Length - 1),
                sel ? ExoColors.PhosphorText : ExoColors.ProksText);
        }
    }

    // ── transmit view ─────────────────────────────────────────────────────────
    //
    // Three boxes appear/disappear in sequence:
    //   [0, TxHoldMs)                               : TRANSMITTING box — TX arrow
    //   [TxHoldMs, typingStart)                     : blank (gap)
    //   [typingStart, typingStart+typingDurationMs) : RECIPIENT TYPING box — dots
    //   [typingStart+typingDurationMs, rxStart)     : blank (gap)
    //   [rxStart, rxStart+RxHoldMs)                 : TRANSMITTING box — RX arrow
    //   rxStart+RxHoldMs                            : CommitReply()

    private void RenderTransmitContent(IRenderBuffer buffer, int borderX, int x, int top,
                                        int visibleRows, int innerW)
    {
        const int TxRows = 4;

        int typingStart = TxHoldMs + SentMs + Gap1Ms;
        int rxStart     = typingStart + _typingDurationMs + Gap2Ms;

        // After TX phase, switch to preview thread (shows the sent reply in the log)
        var contextLines = _txElapsedMs < TxHoldMs ? _threadLines : _previewThreadLines;
        int showLines = Math.Max(0, visibleRows - TxRows);
        int lineStart = Math.Max(0, contextLines.Count - showLines);

        for (int i = 0; i < showLines; i++)
        {
            int li = lineStart + i;
            if (li >= contextLines.Count) break;
            var (text, color) = ParseLine(contextLines[li], innerW);
            buffer.WriteAt(x, top + i, text, color);
        }

        int txBase = top + showLines;

        string recipient  = _rootMessage?.SenderDisplay ?? "REMOTE";
        string operatorId = _account.Login.ToUpper();

        if (_txElapsedMs < TxHoldMs)
        {
            // Phase 1: TX arrow — 8-frame discrete growth, left → right
            int txDashMax = Math.Max(0, innerW - 4 - recipient.Length);
            int frame     = Math.Min(FrameCount - 1, _txElapsedMs / FrameMs);
            int txDashes  = FrameCount > 1 ? txDashMax * frame / (FrameCount - 1) : txDashMax;

            Ui.WriteTransmitBorder(buffer, borderX, txBase, innerW, "─ TRANSMITTING ");
            buffer.WriteAt(x, txBase + 2,
                ("  " + new string('─', txDashes) + "► " + recipient).PadRight(innerW),
                ExoColors.ProksPale);
        }
        else if (_txElapsedMs < TxHoldMs + SentMs)
        {
            // Phase 2: MESSAGE SENT — reply appears in chatlog above, box confirms below
            Ui.WriteTransmitBorder(buffer, borderX, txBase, innerW, "─ MESSAGE SENT ");
            if (_chosenOption != null)
                buffer.WriteAt(x, txBase + 2,
                    Ui.Truncate("  > " + _chosenOption.Text, innerW).PadRight(innerW),
                    ExoColors.PhosphorDim);
        }
        else if (_txElapsedMs >= typingStart && _txElapsedMs < typingStart + _typingDurationMs)
        {
            // Phase 3: Typing indicator — messenger-style dots attributed to recipient
            string typingLabel = "─ " + recipient.ToUpper() + " TYPING ";
            Ui.WriteTransmitBorder(buffer, borderX, txBase, innerW, typingLabel);

            const int DotStepMs = 400;
            int dotStep = (_txElapsedMs - typingStart) / DotStepMs % 3;
            string dots = dotStep == 0 ? ".  " : dotStep == 1 ? ".. " : "...";
            buffer.WriteAt(x + 2, txBase + 2, dots, ExoColors.ProksDark);
        }
        else if (_txElapsedMs >= rxStart)
        {
            // Phase 4: RX arrow — 8-frame discrete growth, right-anchored (right → left)
            int rxElapsed = _txElapsedMs - rxStart;
            int rxDashMax = Math.Max(0, innerW - 2 - recipient.Length);
            int rxFrame   = Math.Min(FrameCount - 1, rxElapsed / FrameMs);
            int rxDashes  = FrameCount > 1 ? rxDashMax * rxFrame / (FrameCount - 1) : rxDashMax;

            string rxLine = "◄" + new string('─', rxDashes) + " " + recipient;
            int    rxX    = x + innerW - rxLine.Length;
            Ui.WriteTransmitBorder(buffer, borderX, txBase, innerW, "─ TRANSMITTING ");
            buffer.WriteAt(x,               txBase + 2, "".PadRight(innerW), ExoColors.ProksDark);
            buffer.WriteAt(Math.Max(x, rxX), txBase + 2, rxLine,             ExoColors.SignalDim);
        }
        // Gap phases: rows left blank (buffer resets each frame)
    }

    private void RebuildPreviewLines()
    {
        _previewThreadLines = new List<string>(_threadLines);
        if (_chosenOption != null)
        {
            _previewThreadLines.Add("EMP:");
            _previewThreadLines.Add("SEP:");
            _previewThreadLines.Add("YOU:  > " + _chosenOption.Text);
            _previewThreadLines.Add("SEP:");
        }
    }

    // ── hints bar ─────────────────────────────────────────────────────────────

    private void RenderHints(IRenderBuffer buffer, int y, int centerX)
    {
        string hints = _state switch
        {
            CommState.List          => "↑↓  navigate     ENTER  open     ESC  back",
            CommState.Reading       => "↑↓  scroll     R  reply     ESC  inbox",
            CommState.ChoosingReply => "↑↓ / 1-4  select     ENTER  send     ESC  cancel",
            _                       => ""
        };
        if (hints.Length == 0) return;
        buffer.WriteAt(centerX - hints.Length / 2, y, hints, ExoColors.ProksDark);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private List<string> BuildThreadLines(List<CommsMessage> chain, int wrapWidth)
    {
        var lines = new List<string>();
        for (int i = 0; i < chain.Count; i++)
        {
            if (i > 0) lines.Add("EMP:");
            foreach (var l in WrapText(chain[i].Body.Trim(), wrapWidth))
                lines.Add("MSG:" + l);

            if (i < chain.Count - 1)
            {
                var chosenId = _repo.GetChosenReply(chain[i].Id);
                var chosen   = chain[i].ReplyOptions.FirstOrDefault(r => r.Id == chosenId);
                lines.Add("EMP:");
                lines.Add("SEP:");
                if (chosen != null) lines.Add("YOU:  > " + chosen.Text);
                lines.Add("SEP:");
            }
        }
        return lines;
    }

    private static (string text, string color) ParseLine(string tagged, int innerW)
    {
        if (tagged.StartsWith("EMP:"))
            return ("".PadRight(innerW), ExoColors.ProksDark);

        if (tagged.StartsWith("SEP:"))
        {
            string sep = "  " + new string('─', Math.Max(0, innerW - 4));
            return (sep.PadRight(innerW), ExoColors.ProksBorder);
        }

        if (tagged.StartsWith("YOU:"))
        {
            string t = tagged[4..];
            return (Ui.Truncate(t, innerW).PadRight(innerW), ExoColors.PhosphorText);
        }

        if (tagged.StartsWith("MSG:"))
        {
            string t = "  " + tagged[4..];
            return (Ui.Truncate(t, innerW).PadRight(innerW), ExoColors.ProksText);
        }

        return ("".PadRight(innerW), ExoColors.ProksDark);
    }

    private static List<string> WrapText(string text, int width)
    {
        var result = new List<string>();
        foreach (var rawLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0) { result.Add(""); continue; }
            if (line.Length <= width) { result.Add(line); continue; }

            var words = line.Split(' ');
            var cur   = "";
            foreach (var word in words)
            {
                if (cur.Length == 0) cur = word;
                else if (cur.Length + 1 + word.Length <= width) cur += " " + word;
                else { result.Add(cur); cur = word; }
            }
            if (cur.Length > 0) result.Add(cur);
        }
        return result;
    }


}
