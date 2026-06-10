using ExoProxy.Core;
using ExoProxy.Data;

namespace ExoProxy.Presentation.Screens.Base.Sections;

public sealed class MemorySection : IBaseSection
{
    public string SectionId => "memory";
    public BaseSectionResponse Response { get; private set; } = new(BaseSectionRequest.Stay, null);

    private enum MemState { TapeSelect, FileList, FileRead, SendSelect, SendConfirm, DeleteConfirm, Transferring, Defragging }
    private MemState _state = MemState.TapeSelect;

    private readonly MemoryRepository _repo;
    private readonly OperatorAccount  _account;
    private readonly GameSettings     _settings;

    // ── tape panel ────────────────────────────────────────────────────────────
    private int _tapeIdx = 0;
    private static readonly string[] _locs     = ["rover", "local", "suirdc"];
    private static readonly string[] _locNames = ["ROVER SR-74", "LOCAL STATION", "SUIRDC UPLINK"];

    // ── file list ─────────────────────────────────────────────────────────────
    private List<MemoryFile> _listFiles  = [];
    private int              _listIdx    = 0;
    private int              _listScroll = 0;
    private const int ListVisible     = 3;
    // Rover(3) + arrow(1) + Local(3) + arrow(1) + SUIRDC(2) — no uniform stride for SUIRDC
    private const int TapeSectionRows = 10;

    // ── file reader ───────────────────────────────────────────────────────────
    private List<string> _readerLines  = [];
    private int          _readerScroll = 0;

    // ── send panel ────────────────────────────────────────────────────────────
    private int      _sendIdx     = 0;
    private string[] _sendOptions = [];
    private string[] _sendTargets = [];

    // ── delete confirm ────────────────────────────────────────────────────────
    private MemoryFile? _deleteTarget = null;

    // ── transfer animation ────────────────────────────────────────────────────
    private string         _transferDst      = "";
    private string         _transferFileId   = "";
    private float          _transferProgress = 0f;
    private DateTimeOffset _transferStart;
    private const int      TransferMs        = 1800;

    // ── defrag animation ──────────────────────────────────────────────────────
    private string         _defragLoc    = "rover";
    private List<string?>  _defragCur    = [];
    private List<string?>  _defragTarget = [];
    private DateTimeOffset _defragTimer;
    private const int DefragStepMs = 70;

    // ── status message ────────────────────────────────────────────────────────
    private string?        _statusMsg   = null;
    private bool           _statusError = false;
    private DateTimeOffset _statusTime;
    private const int StatusMs = 2500;

    // ── blink ─────────────────────────────────────────────────────────────────
    private bool           _blinkOn  = true;
    private DateTimeOffset _blinkTimer;
    private const int BlinkMs = 500;

    // ── layout constants ──────────────────────────────────────────────────────
    private const int BarWidth = 14;
    private const int LW       = 4;

    public MemorySection(OperatorAccount account, MemoryRepository repo, GameSettings settings)
    {
        _account    = account;
        _repo       = repo;
        _settings   = settings;
        _blinkTimer = DateTimeOffset.UtcNow;
        RefreshFileList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Update
    // ─────────────────────────────────────────────────────────────────────────

    public void Update(DateTimeOffset now, InputEvent? input)
    {
        Response = new(BaseSectionRequest.Stay, null);

        if (now - _blinkTimer >= TimeSpan.FromMilliseconds(BlinkMs))
        {
            _blinkOn    = !_blinkOn;
            _blinkTimer = now;
        }

        if (_statusMsg != null && (now - _statusTime).TotalMilliseconds >= StatusMs)
            _statusMsg = null;

        if (_state == MemState.Defragging)
        {
            if (now - _defragTimer >= TimeSpan.FromMilliseconds(DefragStepMs))
            {
                _defragTimer = now;
                if (!DefragStep())
                {
                    _repo.ApplyLayout(_defragLoc, _defragCur);
                    _state = MemState.TapeSelect;
                    RefreshFileList();
                    ShowStatus("DEFRAGMENTATION COMPLETE", false);
                }
            }
            return;
        }

        if (_state == MemState.Transferring)
        {
            float elapsed = (float)(now - _transferStart).TotalMilliseconds;
            _transferProgress = Math.Min(1f, elapsed / TransferMs);
            if (_transferProgress >= 1f)
            {
                bool ok = _transferDst == "local"  ? _repo.MoveToLocal(_transferFileId)  :
                          _transferDst == "suirdc" ? _repo.SyncToSuirdc(_transferFileId) : false;

                string dstName = _transferDst == "local" ? "LOCAL STATION" : "SUIRDC UPLINK";
                ShowStatus(ok
                    ? $"TRANSFER COMPLETE — STORED IN {dstName}"
                    : "TRANSFER FAILED — NO SPACE OR ALREADY EXISTS", !ok);

                _state = MemState.FileList;
                RefreshFileList();
            }
            return;
        }

        if (input is null) return;
        var key = input.Value.Key;

        switch (_state)
        {
            case MemState.TapeSelect:    HandleTapeKey(key);          break;
            case MemState.FileList:      HandleListKey(key);          break;
            case MemState.FileRead:      HandleReadKey(key);          break;
            case MemState.SendSelect:    HandleSendKey(key);          break;
            case MemState.SendConfirm:   HandleSendConfirmKey(key);   break;
            case MemState.DeleteConfirm: HandleDeleteConfirmKey(key); break;
        }
    }

    private void HandleTapeKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape) { Response = new(BaseSectionRequest.GoToHub, null); return; }
        if (key.Key == ConsoleKey.UpArrow)   { _tapeIdx = Math.Max(0, _tapeIdx - 1); RefreshFileList(); return; }
        if (key.Key == ConsoleKey.DownArrow) { _tapeIdx = Math.Min(2, _tapeIdx + 1); RefreshFileList(); return; }
        if (key.Key == ConsoleKey.Enter)
        {
            if (_listFiles.Count == 0) { ShowStatus("NO FILES IN THIS LOCATION", false); return; }
            _listIdx = 0;
            _state   = MemState.FileList;
            return;
        }
        if ((key.KeyChar == 'f' || key.KeyChar == 'F') && _tapeIdx < 2)
            StartDefrag();
    }

    private void HandleListKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape) { _state = MemState.TapeSelect; return; }
        if (key.Key == ConsoleKey.UpArrow   && _listIdx > 0)                    { _listIdx--; return; }
        if (key.Key == ConsoleKey.DownArrow && _listIdx < _listFiles.Count - 1) { _listIdx++; return; }

        if (key.Key == ConsoleKey.Enter && _listFiles.Count > 0)
        {
            BuildReaderLines(_listFiles[_listIdx]);
            _readerScroll = 0;
            _state = MemState.FileRead;
            return;
        }
        if ((key.KeyChar == 's' || key.KeyChar == 'S') && _listFiles.Count > 0)
        {
            OpenSendPanel();
            return;
        }
        if ((key.KeyChar == 'd' || key.KeyChar == 'D') && _listFiles.Count > 0)
        {
            if (_locs[_tapeIdx] == "suirdc") { ShowStatus("SUIRDC FILES ARE READ-ONLY", true); return; }
            _deleteTarget = _listFiles[_listIdx];
            _state        = MemState.DeleteConfirm;
            return;
        }
        if ((key.KeyChar == 'f' || key.KeyChar == 'F') && _tapeIdx < 2)
            StartDefrag();
    }

    private void HandleReadKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)    { _state = MemState.FileList; return; }
        if (key.Key == ConsoleKey.UpArrow)   { if (_readerScroll > 0) _readerScroll--; return; }
        if (key.Key == ConsoleKey.DownArrow) { _readerScroll++; return; }
    }

    private void HandleSendKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)                                           { _state = MemState.FileList; return; }
        if (key.Key == ConsoleKey.UpArrow   && _sendIdx > 0)                       { _sendIdx--; return; }
        if (key.Key == ConsoleKey.DownArrow && _sendIdx < _sendOptions.Length - 1) { _sendIdx++; return; }
        if (key.Key == ConsoleKey.Enter)                                            { _state = MemState.SendConfirm; }
    }

    private void HandleSendConfirmKey(ConsoleKeyInfo key)
    {
        // ESC/N both return to FileList (not SendSelect) for consistent cancel flow
        if (key.Key == ConsoleKey.Escape || key.KeyChar == 'n' || key.KeyChar == 'N')
        {
            _state = MemState.FileList;
            return;
        }
        if (key.KeyChar == 'y' || key.KeyChar == 'Y')
            ConfirmSend();
    }

    private void HandleDeleteConfirmKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape || key.KeyChar == 'n' || key.KeyChar == 'N')
        {
            _deleteTarget = null;
            _state        = MemState.FileList;
            return;
        }
        if (key.KeyChar == 'y' || key.KeyChar == 'Y')
        {
            if (_deleteTarget != null)
            {
                string loc = _locs[_tapeIdx];
                bool ok = loc == "rover" ? _repo.DeleteFromRover(_deleteTarget.Id)
                                         : _repo.DeleteFromLocal(_deleteTarget.Id);
                if (ok)
                {
                    ShowStatus($"{_deleteTarget.DisplayName} — DELETED", false);
                    RefreshFileList();
                    _listIdx = Math.Clamp(_listIdx, 0, Math.Max(0, _listFiles.Count - 1));
                }
            }
            _deleteTarget = null;
            _state        = MemState.FileList;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Actions
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshFileList()
    {
        string loc  = _locs[_tapeIdx];
        _listFiles  = loc == "suirdc" ? _repo.GetSuirdcFiles() : _repo.GetFilesAt(loc);
        _listIdx    = Math.Clamp(_listIdx, 0, Math.Max(0, _listFiles.Count - 1));
        _listScroll = 0;
    }

    private void BuildReaderLines(MemoryFile file)
    {
        _readerLines = file.Content
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(l => l.TrimEnd())
            .ToList();
    }

    private void StartDefrag()
    {
        string loc    = _locs[_tapeIdx];
        var    layout = _repo.GetLayout(loc);
        var    target = _repo.GetDefragTarget(loc);

        if (layout.SequenceEqual(target)) { ShowStatus("STORAGE ALREADY COMPACTED", false); return; }

        _defragLoc    = loc;
        _defragCur    = new List<string?>(layout);
        _defragTarget = target;
        _defragTimer  = DateTimeOffset.UtcNow;
        _state        = MemState.Defragging;
    }

    private bool DefragStep()
    {
        for (int i = 0; i < _defragCur.Count; i++)
        {
            if (_defragCur[i] == _defragTarget[i]) continue;
            string? want = _defragTarget[i];
            if (want == null) continue;
            for (int j = i + 1; j < _defragCur.Count; j++)
            {
                if (_defragCur[j] == want)
                {
                    (_defragCur[i], _defragCur[j]) = (_defragCur[j], _defragCur[i]);
                    return true;
                }
            }
        }
        return false;
    }

    private void OpenSendPanel()
    {
        string loc = _locs[_tapeIdx];
        if (loc == "suirdc") { ShowStatus("SUIRDC FILES ARE READ-ONLY", true); return; }

        _sendOptions = loc == "rover"
            ? ["MOVE TO LOCAL STATION"]
            : ["SYNC TO SUIRDC UPLINK"];
        _sendTargets = loc == "rover" ? ["local"] : ["suirdc"];
        _sendIdx     = 0;
        _state       = MemState.SendSelect;
    }

    private void ConfirmSend()
    {
        if (_listFiles.Count == 0) return;
        _transferFileId   = _listFiles[_listIdx].Id;
        _transferDst      = _sendTargets[_sendIdx];
        _transferProgress = 0f;
        _transferStart    = DateTimeOffset.UtcNow;
        _state            = MemState.Transferring;
    }

    private void ShowStatus(string msg, bool error)
    {
        _statusMsg   = msg;
        _statusError = error;
        _statusTime  = DateTimeOffset.UtcNow;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Render
    // ─────────────────────────────────────────────────────────────────────────

    public void Render(IRenderBuffer buffer)
    {
        int w      = buffer.Width;
        int h      = buffer.Height;
        int innerW = w - 2;

        int panelTop    = 1;
        int panelBottom = h - 3;
        int hintsRow    = h - 2;

        int tapeStart   = panelTop + 1;
        int tapeSepRow  = tapeStart + TapeSectionRows;
        int listHdrRow  = tapeSepRow + 1;
        int listStart   = tapeSepRow + 2;
        int listSepRow  = listStart + ListVisible;   // no separate status row
        int readerStart = listSepRow + 1;

        WriteBorder(buffer, 0, panelTop, w, " MEMORY ALLOCATION SYSTEM ", $" {_settings.SolDisplay} ");
        buffer.WriteAt(0, panelBottom, "└" + new string('─', innerW) + "┘", ExoColors.ProksBorder);
        for (int r = panelTop + 1; r < panelBottom; r++)
        {
            buffer.WriteAt(0,     r, "│", ExoColors.ProksBorder);
            buffer.WriteAt(w - 1, r, "│", ExoColors.ProksBorder);
        }

        buffer.WriteAt(0, tapeSepRow, "├" + new string('─', innerW) + "┤", ExoColors.ProksBorder);
        buffer.WriteAt(0, listSepRow, "├" + new string('─', innerW) + "┤", ExoColors.ProksBorder);

        var displayLayouts = new Dictionary<string, List<string?>>
        {
            ["rover"] = _state == MemState.Defragging && _defragLoc == "rover" ? _defragCur : _repo.GetLayout("rover"),
            ["local"] = _state == MemState.Defragging && _defragLoc == "local" ? _defragCur : _repo.GetLayout("local"),
        };

        RenderTapeSection(buffer, tapeStart, innerW, displayLayouts);
        RenderFileListSection(buffer, listHdrRow, listStart, innerW);

        if (_state == MemState.TapeSelect)
            RenderTapeSelectReader(buffer, readerStart, panelBottom - 1, innerW);
        else if (_state == MemState.DeleteConfirm && _deleteTarget != null)
            RenderDeleteConfirmPanel(buffer, readerStart, panelBottom - 1, innerW);
        else if (_state == MemState.SendConfirm)
            RenderSendConfirmPanel(buffer, readerStart, panelBottom - 1, innerW);
        else if (_state == MemState.Transferring)
            RenderTransferPanel(buffer, readerStart, panelBottom - 1, innerW);
        else if (_state == MemState.SendSelect)
            RenderSendPanel(buffer, readerStart, panelBottom - 1, innerW);
        else if (_state is MemState.FileRead or MemState.Defragging)
            RenderFileReader(buffer, readerStart, panelBottom - 1, innerW);
        else
            RenderFileListHint(buffer, readerStart, panelBottom - 1, innerW);

        RenderHints(buffer, hintsRow, innerW);
    }

    // ── tape section ──────────────────────────────────────────────────────────

    private void RenderTapeSection(IRenderBuffer buffer, int startRow, int innerW,
        Dictionary<string, List<string?>> layouts)
    {
        int x   = 2;
        int row = startRow;

        for (int i = 0; i < 3; i++)
        {
            string loc  = _locs[i];
            string name = _locNames[i];
            bool   sel  = _tapeIdx == i;

            int hRow = row;
            int tRow = row + 1;

            // Marker blinks PhosphorText / PhosphorDim — same as CommsSection and SettingsSection
            string marker    = sel ? (_blinkOn ? "►" : "▷") : " ";
            string markerCol = sel
                ? (_blinkOn ? ExoColors.PhosphorText : ExoColors.PhosphorDim)
                : ExoColors.ProksBorder;
            string nameCol = sel ? ExoColors.PhosphorText : ExoColors.ProksText;

            buffer.WriteAt(x, hRow, marker, markerCol);

            var files = loc == "suirdc" ? _repo.GetSuirdcFiles() : _repo.GetFilesAt(loc);

            if (loc == "suirdc")
            {
                int cx = x + 2;
                buffer.WriteAt(cx, hRow, name, nameCol);
                cx += name.Length;

                var suirdcIds = files.Select(f => f.Id).ToHashSet();
                int queue     = _repo.GetFilesAt("local").Count(f => !suirdcIds.Contains(f.Id));

                string meta = $"   BW: 32 KB/SOL   QUEUE: {queue}   FILES: {files.Count}";
                buffer.WriteAt(cx, hRow, meta, ExoColors.ProksPale);
                cx += meta.Length;

                // [READ-ONLY] is a system property, not an error — ProksPale
                buffer.WriteAt(cx, hRow, "   [READ-ONLY]", ExoColors.ProksPale);

                RenderSuirdcLine(buffer, x + 2, tRow, innerW - x - 3, files);

                row += 2; // header + tags line (no ruler for tag-based SUIRDC)
            }
            else
            {
                var layout = layouts[loc];
                int used   = layout.Count(b => b != null);
                int cap    = _repo.GetCapacity(loc);
                int frag   = _repo.GetFragmentPercent(loc);

                int cx = x + 2;
                buffer.WriteAt(cx, hRow, name, nameCol);
                cx += name.Length + 2;

                string bar   = BuildUsageBar(used, cap);
                string stats = $"   {used}/{cap} KB";
                buffer.WriteAt(cx, hRow, bar, ExoColors.ProksText);
                cx += bar.Length;

                buffer.WriteAt(cx, hRow, stats, ExoColors.ProksPale);
                cx += stats.Length;

                if (frag > 0)
                    buffer.WriteAt(cx, hRow, $"   [!] FRAG {frag}%", ExoColors.FaultText);

                int rulerRow = row + 2;
                RenderTapeLine(buffer, x + 2, tRow, rulerRow, innerW - x - 3, layout, files);

                row += 3; // header + tape line + ruler
            }

            if (i < 2)
            {
                string arrow = i == 0 ? "↓  MOVE TO LOCAL" : "↓  SYNC TO SUIRDC";
                buffer.WriteAt(innerW / 2 - arrow.Length / 2, row, arrow, ExoColors.ProksPale);
                row++; // arrow row
            }
        }
    }

    // Renders SUIRDC files as named tags — all in ProksPale (machine whispers about its archive)
    private void RenderSuirdcLine(IRenderBuffer buffer, int x, int y, int maxW, List<MemoryFile> files)
    {
        if (files.Count == 0)
        {
            buffer.WriteAt(x, y, "[NO FILES SYNCED]", ExoColors.ProksPale);
            return;
        }

        int curX = x;
        for (int i = 0; i < files.Count; i++)
        {
            var    file  = files[i];
            string tag   = $"[{file.DisplayName}]";
            int    after = files.Count - i - 1;

            bool roomForTag  = curX + tag.Length <= x + maxW;
            bool roomForMore = after == 0 || curX + tag.Length + 1 + $"(+{after} more)".Length <= x + maxW;

            if (!roomForTag || (!roomForMore && after > 0))
            {
                int    n        = files.Count - i;
                string overflow = $"(+{n} more)";
                if (curX + overflow.Length <= x + maxW)
                    buffer.WriteAt(curX, y, overflow, ExoColors.ProksPale);
                break;
            }

            buffer.WriteAt(curX, y, tag, ExoColors.ProksPale);
            curX += tag.Length + 1;
        }
    }

    private static string BuildUsageBar(int used, int capacity)
    {
        int filled = capacity > 0 ? (int)Math.Round((double)used / capacity * BarWidth) : 0;
        filled = Math.Clamp(filled, 0, BarWidth);
        return "[" + new string('─', filled) + new string('·', BarWidth - filled) + "]";
    }

    private void RenderTapeLine(IRenderBuffer buffer, int x, int y, int rulerRow, int maxW,
        List<string?> layout, List<MemoryFile> files)
    {
        var fileLabels = new Dictionary<string, string>();
        foreach (string? fileId in layout.Where(id => id != null).Distinct())
        {
            var file = files.FirstOrDefault(f => f.Id == fileId);
            if (file == null) continue;
            fileLabels[fileId!] = (file.DisplayName.Length > LW
                ? file.DisplayName[..LW]
                : file.DisplayName.PadRight(LW)).ToUpper();
        }

        // Build runs
        var runs = new List<(string? id, int count)>();
        for (int i = 0; i < layout.Count;)
        {
            string? cur = layout[i];
            int     s   = i;
            while (i < layout.Count && layout[i] == cur) i++;
            runs.Add((cur, i - s));
        }

        // Track segment boundaries for the ruler: (xPosition, kbValue)
        var boundaries = new List<(int xPos, int kb)>();

        int curX  = x;
        int curKb = 0;

        buffer.WriteAt(curX++, y, "{", ExoColors.ProksBorder);
        boundaries.Add((x, 0)); // "0" sits under '{'

        foreach (var (id, count) in runs)
        {
            if (curX >= x + maxW - 1) break;

            string seg;
            string col;

            if (id == null)
            {
                // Free space: 2 dots per block — subtle ProksBorder so empty space is visible but quiet
                seg = "[" + new string('·', count * 2) + "]";
                col = ExoColors.ProksBorder;
            }
            else
            {
                // File segment: 2 chars per block — label fills left, dashes extend right
                string label    = fileLabels.GetValueOrDefault(id, "????");
                int    contentW = count * 2;
                string content  = contentW <= label.Length
                    ? label[..contentW]
                    : label + new string('─', contentW - label.Length);
                seg = "[" + content + "]";
                col = ExoColors.ProksText;
            }

            int avail = x + maxW - 1 - curX;
            if (seg.Length > avail) seg = seg[..avail];
            buffer.WriteAt(curX, y, seg, col);
            curX  += seg.Length;
            curKb += count;

            if (curX < x + maxW)
                boundaries.Add((curX, curKb));
        }

        if (curX < x + maxW)
            buffer.WriteAt(curX, y, "}", ExoColors.ProksBorder);

        RenderTapeRuler(buffer, x, rulerRow, maxW, boundaries);
    }

    // Renders KB position markers below the tape line
    private static void RenderTapeRuler(IRenderBuffer buffer, int x, int y, int maxW,
        List<(int xPos, int kb)> boundaries)
    {
        int lastLabelEndX = int.MinValue;

        for (int i = 0; i < boundaries.Count; i++)
        {
            var (bx, kb) = boundaries[i];
            string label = kb.ToString();
            int    rx    = bx - x; // position relative to tape start

            if (rx >= maxW) break;

            // Skip if this label would overlap the previous one
            if (rx < lastLabelEndX + 1) continue;

            buffer.WriteAt(x + rx, y, label, ExoColors.ProksDark);
            lastLabelEndX = rx + label.Length;
        }
    }

    // ── file list section ─────────────────────────────────────────────────────

    private void RenderFileListSection(IRenderBuffer buffer, int hdrRow, int listStart, int innerW)
    {
        int x = 2;

        string tapeName = _locNames[_tapeIdx];
        buffer.WriteAt(x,      hdrRow, $"FILES IN [{tapeName}]", ExoColors.PhosphorText);
        buffer.WriteAt(x + 25, hdrRow, "TYPE",   ExoColors.ProksBorder);
        buffer.WriteAt(x + 31, hdrRow, "KB",     ExoColors.ProksBorder);
        buffer.WriteAt(x + 36, hdrRow, "SOL",    ExoColors.ProksBorder);
        buffer.WriteAt(x + 44, hdrRow, "STATUS", ExoColors.ProksBorder);

        // Status message shown right-aligned in header row — no dedicated status row needed
        if (_statusMsg != null)
        {
            string statusCol = _statusError ? ExoColors.FaultText : ExoColors.PhosphorText;
            string truncated = Truncate(_statusMsg, innerW - 55);
            buffer.WriteAt(innerW - truncated.Length - 1, hdrRow, truncated, statusCol);
        }

        if (_listIdx < _listScroll)               _listScroll = _listIdx;
        if (_listIdx >= _listScroll + ListVisible) _listScroll = _listIdx - ListVisible + 1;
        _listScroll = Math.Max(0, _listScroll);

        bool inList = _state is MemState.FileList or MemState.FileRead
                               or MemState.SendSelect or MemState.SendConfirm or MemState.DeleteConfirm;

        string        loc       = _locs[_tapeIdx];
        List<string?> rowLayout = loc == "suirdc" ? [] : _repo.GetLayout(loc);

        for (int i = 0; i < ListVisible; i++)
        {
            int idx = _listScroll + i;
            if (idx >= _listFiles.Count) break;
            RenderFileRow(buffer, x, listStart + i, _listFiles[idx],
                idx == _listIdx && inList, innerW, rowLayout);
        }

        if (_listScroll > 0)
            buffer.WriteAt(innerW - 6, listStart, "▲ more", ExoColors.ProksPale);
        if (_listFiles.Count > _listScroll + ListVisible)
            buffer.WriteAt(innerW - 6, listStart + ListVisible - 1, "▼ more", ExoColors.ProksPale);
    }

    private void RenderFileRow(IRenderBuffer buffer, int x, int y,
        MemoryFile file, bool selected, int innerW, List<string?> layout)
    {
        string loc    = _locs[_tapeIdx];
        bool   frag   = loc != "suirdc" && _repo.IsFragmented(file.Id, layout);
        string status = frag ? "[!] FRAG" : "OK";

        // Arrow — PhosphorText/PhosphorDim, consistent with CommsSection and SettingsSection
        string arrow    = selected ? (_blinkOn ? "►" : "▷") : " ";
        string arrowCol = selected
            ? (_blinkOn ? ExoColors.PhosphorText : ExoColors.PhosphorDim)
            : ExoColors.ProksBorder;

        string rowCol = selected ? ExoColors.PhosphorText
                      : (frag   ? ExoColors.FaultText : ExoColors.ProksText);

        string name   = Truncate(file.DisplayName, 22).PadRight(22);
        string type   = file.Type.PadRight(5);
        string blocks = file.Blocks.ToString();          // left-align under K of KB header
        string solNum = file.Sol.StartsWith("SOL ") ? file.Sol[4..] : file.Sol;
        string sol    = solNum.PadLeft(3);

        buffer.WriteAt(x,      y, arrow,  arrowCol);
        buffer.WriteAt(x + 2,  y, name,   rowCol);

        // Metadata columns — ProksBorder (structural info, not primary data)
        buffer.WriteAt(x + 25, y, type,   ExoColors.ProksBorder);
        buffer.WriteAt(x + 31, y, blocks, ExoColors.ProksBorder);
        buffer.WriteAt(x + 36, y, sol,    ExoColors.ProksBorder);

        // Status: FRAG = FaultText (warning), OK = ProksBorder (background info, not important)
        buffer.WriteAt(x + 44, y, status, frag ? ExoColors.FaultText : ExoColors.ProksBorder);
    }

    // ── send panel (reader area) ──────────────────────────────────────────────

    private void RenderSendPanel(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        if (_listFiles.Count == 0 || _listIdx >= _listFiles.Count) return;

        var    file     = _listFiles[_listIdx];
        int    fullW    = innerW + 2;
        int    x        = 2;
        string destLoc  = _sendTargets.Length > 0 ? _sendTargets[_sendIdx] : "";

        WriteTransmitBorder(buffer, 0, startRow, fullW, "─ SEND FILE ");

        buffer.WriteAt(x, startRow + 1,
            $"  {file.DisplayName} [{file.Type}]  —  {file.Blocks} KB",
            ExoColors.PhosphorText);
        buffer.WriteAt(x, startRow + 2,
            new string('─', Math.Min(50, innerW - x)), ExoColors.ProksBorder);

        for (int i = 0; i < _sendOptions.Length; i++)
        {
            int row = startRow + 3 + i;
            if (row > endRow) break;
            bool   sel  = i == _sendIdx;
            string arr  = sel ? (_blinkOn ? "►" : "▷") : " ";
            string col  = sel ? ExoColors.PhosphorText : ExoColors.ProksText;
            string acol = sel ? (_blinkOn ? ExoColors.PhosphorText : ExoColors.PhosphorDim)
                              : ExoColors.ProksBorder;
            buffer.WriteAt(x,     row, arr,             acol);
            buffer.WriteAt(x + 2, row, _sendOptions[i], col);
        }

        int infoRow = startRow + 3 + _sendOptions.Length + 1;

        // Show destination free space so the player can judge before confirming
        if (destLoc != "suirdc" && infoRow <= endRow)
        {
            var  destLayout = _repo.GetLayout(destLoc);
            int  destCap    = _repo.GetCapacity(destLoc);
            int  destFree   = destCap - destLayout.Count(b => b != null);
            bool fits       = file.Blocks <= destFree;

            string freeInfo = $"  DST FREE: {destFree} KB" + (fits ? "" : "  — INSUFFICIENT SPACE");
            string freeCol  = fits ? ExoColors.ProksPale : ExoColors.FaultText;
            buffer.WriteAt(x, infoRow, freeInfo, freeCol);
            infoRow++;
        }

        // MOVE warning is shown here so the player sees it before the confirm step
        if (infoRow + 1 <= endRow)
        {
            buffer.WriteAt(x, infoRow,     new string('─', Math.Min(50, innerW - x)), ExoColors.ProksBorder);
            buffer.WriteAt(x, infoRow + 1, "  NOTE: FILE WILL BE REMOVED FROM SOURCE.", ExoColors.FaultText);
        }
    }

    // ── send confirm panel ────────────────────────────────────────────────────

    private void RenderSendConfirmPanel(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        if (_listFiles.Count == 0 || _listIdx >= _listFiles.Count) return;

        var    file    = _listFiles[_listIdx];
        int    fullW   = innerW + 2;
        int    x       = 2;
        string dstName = _sendTargets.Length > 0
            ? (_sendTargets[_sendIdx] == "local" ? "LOCAL STATION" : "SUIRDC UPLINK")
            : "UNKNOWN";

        WriteTransmitBorder(buffer, 0, startRow, fullW, "─ CONFIRM TRANSFER ");

        int row = startRow + 1;
        buffer.WriteAt(x, row++, $"  {file.DisplayName} [{file.Type}]  —  {file.Blocks} KB",
            ExoColors.PhosphorText);
        buffer.WriteAt(x, row++, new string('─', Math.Min(50, innerW - x)), ExoColors.ProksBorder);
        buffer.WriteAt(x, row++, $"  DESTINATION: {dstName}",               ExoColors.ProksText);
        buffer.WriteAt(x, row++, "  FILE WILL BE REMOVED FROM SOURCE.",      ExoColors.FaultText);
        buffer.WriteAt(x, row++, new string('─', Math.Min(50, innerW - x)), ExoColors.ProksBorder);
        if (row <= endRow) buffer.WriteAt(x, row++, "  [Y]  CONFIRM TRANSFER", ExoColors.PhosphorBright);
        if (row <= endRow) buffer.WriteAt(x, row,   "  [N]  CANCEL",           ExoColors.PhosphorText);
    }

    // ── delete confirm panel ──────────────────────────────────────────────────

    private void RenderDeleteConfirmPanel(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        if (_deleteTarget == null) return;
        int midRow  = startRow + (endRow - startRow) / 2;
        int sepW    = Math.Min(44, innerW - 4);
        int usableW = innerW - 2;
        int cx(int len) => 2 + Math.Max(0, (usableW - len) / 2);

        for (int r = startRow; r <= endRow; r++)
            buffer.WriteAt(2, r, new string(' ', innerW - 2), ExoColors.ProksDark);

        string loc      = _locNames[_tapeIdx];
        string title    = $"DELETE FROM [{loc}]";
        string fileInfo = $"FILE: {_deleteTarget.DisplayName}";
        string warning  = "THIS ACTION CANNOT BE UNDONE.";
        string yLine    = "[Y]  CONFIRM DELETE";
        string nLine    = "[N]  CANCEL";

        buffer.WriteAt(cx(title.Length),    midRow - 2, title,                  ExoColors.FaultText);
        buffer.WriteAt(cx(sepW),            midRow - 1, new string('─', sepW),  ExoColors.ProksBorder);
        buffer.WriteAt(cx(fileInfo.Length), midRow,     fileInfo,               ExoColors.PhosphorBright);
        buffer.WriteAt(cx(warning.Length),  midRow + 1, warning,                ExoColors.FaultText);
        buffer.WriteAt(cx(sepW),            midRow + 2, new string('─', sepW),  ExoColors.ProksBorder);
        if (midRow + 3 <= endRow)
            buffer.WriteAt(cx(yLine.Length), midRow + 3, yLine,                 ExoColors.FaultBright);
        if (midRow + 4 <= endRow)
            buffer.WriteAt(cx(nLine.Length), midRow + 4, nLine,                 ExoColors.PhosphorText);
    }

    // ── transfer panel ────────────────────────────────────────────────────────

    private void RenderTransferPanel(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        if (endRow - startRow < 3) return;

        int    fullW   = innerW + 2;
        int    x       = 2;
        string srcName = _repo.GetFile(_transferFileId)?.DisplayName ?? _transferFileId.ToUpper();
        string dstName = _transferDst == "local" ? "LOCAL STATION" : "SUIRDC UPLINK";

        int    arrowSuffix = 2 + dstName.Length;
        int    dashMax     = Math.Max(0, innerW - arrowSuffix - 4);
        int    dashes      = (int)(_transferProgress * dashMax);
        string arrowLine   = "  " + new string('─', dashes) + "► " + dstName;

        WriteTransmitBorder(buffer, 0, startRow,     fullW, "─ TRANSMITTING ");
        buffer.WriteAt(x, startRow + 1,
            Truncate($"  {srcName}", innerW).PadRight(innerW), ExoColors.PhosphorText);
        buffer.WriteAt(x, startRow + 2,
            arrowLine.PadRight(Math.Min(innerW, arrowLine.Length + 1)), ExoColors.ProksPale);
        if (startRow + 3 <= endRow)
            WriteTransmitBorder(buffer, 0, startRow + 3, fullW, "");
    }

    private static void WriteTransmitBorder(IRenderBuffer buffer, int col, int row,
        int fullWidth, string label)
    {
        int dashCount = Math.Max(0, fullWidth - 2 - label.Length);
        buffer.WriteAt(col, row,
            "├" + label + new string('─', dashCount) + "┤",
            ExoColors.ProksBorder);
    }

    // ── file list hint (reader area when no file is open) ────────────────────

    private static void RenderFileListHint(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        int midRow = startRow + (endRow - startRow) / 2;
        int x      = 2;
        string msg = "PRESS ENTER TO READ FILE";
        buffer.WriteAt(x + (innerW - x - msg.Length) / 2, midRow, msg, ExoColors.ProksBorder);
    }

    // ── tape select reader placeholder ───────────────────────────────────────

    private static void RenderTapeSelectReader(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        int midRow = startRow + (endRow - startRow) / 2;
        int x      = 2;

        string msg = "SELECT A STORAGE LOCATION";
        string sub = "↑↓  NAVIGATE     ENTER  OPEN FILES";

        buffer.WriteAt(x + (innerW - x - msg.Length) / 2, midRow - 1, msg, ExoColors.ProksText);
        buffer.WriteAt(x + (innerW - x - sub.Length) / 2, midRow + 1, sub, ExoColors.ProksBorder);
    }

    // ── file reader ───────────────────────────────────────────────────────────

    private void RenderFileReader(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        if (_listFiles.Count == 0) return;

        var file = _listIdx < _listFiles.Count ? _listFiles[_listIdx] : null;
        if (file == null) return;

        int  x         = 2;
        bool isPreview = _state != MemState.FileRead;

        // Label and title colors distinguish preview from active reading
        string label    = isPreview ? "[PREVIEW]" : "[READING]";
        string labelCol = isPreview ? ExoColors.ProksBorder : ExoColors.PhosphorDim;
        string titleCol = isPreview ? ExoColors.ProksText   : ExoColors.PhosphorText;
        string titleLine = Truncate($"{file.DisplayName} [{file.Type}]  —  {file.Description}", innerW - x - 12);

        buffer.WriteAt(x, startRow, titleLine, titleCol);
        buffer.WriteAt(innerW - label.Length, startRow, label, labelCol);

        buffer.WriteAt(x, startRow + 1,
            new string('─', Math.Min(innerW - x, 60)), ExoColors.ProksBorder);

        int contentTop = startRow + 2;
        int maxRows    = endRow - contentTop + 1;
        if (maxRows <= 0) return;

        if (isPreview)
        {
            _readerScroll = 0;
        }
        else
        {
            int maxScroll = Math.Max(0, _readerLines.Count - maxRows);
            _readerScroll = Math.Clamp(_readerScroll, 0, maxScroll);
        }

        bool hasAbove = !isPreview && _readerScroll > 0;
        bool hasBelow = !isPreview && (_readerScroll + maxRows < _readerLines.Count);

        for (int i = 0; i < maxRows; i++)
        {
            int li  = _readerScroll + i;
            if (li >= _readerLines.Count) break;
            int row = contentTop + i;

            string line = Truncate(_readerLines[li], innerW - x - 8);
            buffer.WriteAt(x, row, line, ExoColors.ProksText);

            // Scroll indicators on the right of first/last visible content row
            if (hasAbove && i == 0)
                buffer.WriteAt(innerW - 7, row, "▲ more", ExoColors.ProksPale);
            else if (hasBelow && i == maxRows - 1)
                buffer.WriteAt(innerW - 7, row, "▼ more", ExoColors.ProksPale);
        }

        if (!isPreview && _readerLines.Count > 0)
        {
            int    page  = _readerScroll / Math.Max(1, maxRows) + 1;
            int    total = (_readerLines.Count - 1) / Math.Max(1, maxRows) + 1;
            string ind   = $"↕ {page}/{total}";
            buffer.WriteAt(innerW - ind.Length - 1, endRow, ind, ExoColors.ProksBorder);
        }
    }

    // ── hints ─────────────────────────────────────────────────────────────────

    private void RenderHints(IRenderBuffer buffer, int y, int innerW)
    {
        string hints = _state switch
        {
            MemState.TapeSelect    => _tapeIdx < 2
                                       ? "↑↓  select tape     ENTER  open     F  defrag     ESC  back to hub"
                                       : "↑↓  select tape     ENTER  open files     ESC  back to hub",
            MemState.FileList      => _tapeIdx < 2
                                       ? "↑↓  navigate     ENTER  read     S  send     D  delete     F  defrag     ESC  tape select"
                                       : "↑↓  navigate     ENTER  read     ESC  tape select",
            MemState.FileRead      => "↑↓  scroll     ESC  back to list",
            MemState.SendSelect    => "↑↓  select target     ENTER  confirm     ESC  cancel",
            MemState.SendConfirm   => "Y  confirm transfer     N / ESC  cancel",
            MemState.DeleteConfirm => "Y  confirm delete     N / ESC  cancel",
            MemState.Transferring  => "TRANSFER IN PROGRESS — PLEASE WAIT",
            MemState.Defragging    => "DEFRAGMENTING — PLEASE WAIT",
            _                      => ""
        };
        if (hints.Length == 0) return;
        int hintX = Math.Max(0, (innerW - hints.Length) / 2);
        buffer.WriteAt(hintX, y, hints, ExoColors.ProksPale);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void WriteBorder(IRenderBuffer buffer, int x, int y, int w,
        string leftTitle, string rightTitle)
    {
        int inner   = w - 2;
        int midDash = Math.Max(1, inner - 1 - leftTitle.Length - rightTitle.Length - 1);

        buffer.WriteAt(x, y, "┌─", ExoColors.ProksBorder);
        int cur = x + 2;
        buffer.WriteAt(cur, y, leftTitle, ExoColors.ProksText);
        cur += leftTitle.Length;
        buffer.WriteAt(cur, y, new string('─', midDash), ExoColors.ProksBorder);
        cur += midDash;
        buffer.WriteAt(cur, y, rightTitle, ExoColors.SignalText);
        cur += rightTitle.Length;
        buffer.WriteAt(cur, y, "─┐", ExoColors.ProksBorder);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..Math.Max(0, max - 1)] + "…";
}
