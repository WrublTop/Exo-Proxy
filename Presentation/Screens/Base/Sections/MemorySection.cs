using ExoProxy.Core;
using ExoProxy.Data;

namespace ExoProxy.Presentation.Screens.Base.Sections;

// Tape-based file management across three storage locations:
//   ROVER SR-74 (32 KB)  →  LOCAL STATION (32 KB)  →  SUIRDC UPLINK (16 KB, read-only)
// Files MOVE between locations (never copy). SUIRDC sync is irreversible.
// Layout at 120×30: tapes (rows 2-11), file list (13-17), reader/modal area (19-26).
public sealed class MemorySection : IBaseSection
{
    public string SectionId => SectionIds.Memory;
    public BaseSectionResponse Response { get; private set; } = new(BaseSectionRequest.Stay, null);

    private enum MemState { TapeSelect, FileList, FileRead, SendConfirm, DeleteConfirm, Transferring, Defragging }
    private MemState _state = MemState.TapeSelect;

    private readonly MemoryRepository _repo;
    private readonly OperatorAccount  _account;
    private readonly OperatorProgress _progress;

    // ── tape panel ────────────────────────────────────────────────────────────
    private int _tapeIdx = 0;
    private static readonly StorageLocation[] _locs =
        [StorageLocation.Rover, StorageLocation.Local, StorageLocation.Suirdc];
    private static readonly string[] _locNames = ["ROVER SR-74", "LOCAL STATION", "SUIRDC UPLINK"];

    // ── file list ─────────────────────────────────────────────────────────────
    private List<MemoryFile> _listFiles  = [];
    private int              _listIdx    = 0;
    private int              _listScroll = 0;
    private const int ListVisible     = 4;
    private const int TapeSectionRows = 10;

    // ── file reader ───────────────────────────────────────────────────────────
    private List<string> _readerLines  = [];
    private int          _readerScroll = 0;

    // ── send confirm ──────────────────────────────────────────────────────────
    private StorageLocation _sendDst = StorageLocation.Local;

    // ── delete confirm ────────────────────────────────────────────────────────
    private MemoryFile? _deleteTarget = null;

    // ── transfer animation ────────────────────────────────────────────────────
    private StorageLocation _transferDst      = StorageLocation.Local;
    private string          _transferFileId   = "";
    private float           _transferProgress = 0f;
    private TimeSpan        _transferStart;
    private int             _transferMs       = 1800;

    // ── defrag ────────────────────────────────────────────────────────────────
    private StorageLocation _defragLoc    = StorageLocation.Rover;
    private List<string?>   _defragCur    = [];
    private List<string?>   _defragTarget = [];
    private TimeSpan        _defragTimer;
    private int             _defragMoves;
    private int             _defragTotal;
    private MemState        _defragReturn = MemState.TapeSelect;
    private const int DefragStepMs = 70;

    // ── status message ────────────────────────────────────────────────────────
    private string?  _statusMsg   = null;
    private bool     _statusError = false;
    private TimeSpan _statusTime;
    private const int StatusMs = 2500;

    // ── blink / clock ─────────────────────────────────────────────────────────
    private BlinkState _blink;
    private TimeSpan   _now;         // last Update tick — input handlers read this

    // ── layout constants ──────────────────────────────────────────────────────
    private const int BarWidth = 14;
    private const int LW       = 4;

    public MemorySection(OperatorAccount account, MemoryRepository repo, OperatorProgress progress)
    {
        _account  = account;
        _repo     = repo;
        _progress = progress;
        RefreshFileList();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update(GameTime time, InputEvent? input)
    {
        var now = time.Total;
        _now = now;
        _blink.Update(now);

        Response = new(BaseSectionRequest.Stay, null);

        if (_statusMsg != null && (now - _statusTime).TotalMilliseconds >= StatusMs)
            _statusMsg = null;

        if (_state == MemState.Defragging)   { TickDefrag(now);   return; }
        if (_state == MemState.Transferring) { TickTransfer(now); return; }

        if (input is null) return;
        var key = input.Value.Key;

        switch (_state)
        {
            case MemState.TapeSelect:    HandleTapeKey(key);          break;
            case MemState.FileList:      HandleListKey(key);          break;
            case MemState.FileRead:      HandleReadKey(key);          break;
            case MemState.SendConfirm:   HandleSendConfirmKey(key);   break;
            case MemState.DeleteConfirm: HandleDeleteConfirmKey(key); break;
        }
    }

    private void TickDefrag(TimeSpan now)
    {
        if (now - _defragTimer < TimeSpan.FromMilliseconds(DefragStepMs)) return;
        _defragTimer = now;

        if (DefragStep()) { _defragMoves++; return; }

        _repo.ApplyLayout(_defragLoc, _defragCur);
        _state = _defragReturn;
        RefreshFileList();
        ShowStatus($"DEFRAGMENTATION COMPLETE — {_defragMoves} BLOCKS RELOCATED", false);
    }

    private void TickTransfer(TimeSpan now)
    {
        float elapsed = (float)(now - _transferStart).TotalMilliseconds;
        _transferProgress = Math.Min(1f, elapsed / _transferMs);
        if (_transferProgress < 1f) return;

        var result = _transferDst == StorageLocation.Local
            ? _repo.MoveToLocal(_transferFileId)
            : _repo.SyncToSuirdc(_transferFileId);

        string dstName = _locNames[(int)_transferDst];
        var (msg, isError) = result switch
        {
            TransferResult.Ok                => ($"TRANSFER COMPLETE — STORED IN {dstName}", false),
            TransferResult.InsufficientSpace => ("TRANSFER REJECTED — INSUFFICIENT BLOCKS AT DESTINATION", true),
            TransferResult.AlreadyStored     => ("TRANSFER REJECTED — DUPLICATE RECORD AT DESTINATION", true),
            _                                => ("TRANSFER REJECTED — SOURCE RECORD MISSING", true),
        };
        ShowStatus(msg, isError);
        _state = MemState.FileList;
        RefreshFileList();
    }

    private void HandleTapeKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape) { Response = new(BaseSectionRequest.GoToHub, null); return; }
        if (key.Key == ConsoleKey.UpArrow)   { _tapeIdx = Math.Max(0, _tapeIdx - 1); RefreshFileList(); return; }
        if (key.Key == ConsoleKey.DownArrow) { _tapeIdx = Math.Min(2, _tapeIdx + 1); RefreshFileList(); return; }
        if (key.Key == ConsoleKey.Enter)
        {
            if (_listFiles.Count == 0)
            {
                ShowStatus(_locs[_tapeIdx] == StorageLocation.Suirdc
                    ? "ARCHIVE EMPTY" : "NO RECORDS ON TAPE", false);
                return;
            }
            _listIdx = 0;
            _state   = MemState.FileList;
            return;
        }
        if ((key.KeyChar == 'f' || key.KeyChar == 'F') && _tapeIdx < 2)
            StartDefrag(MemState.TapeSelect);
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
            OpenSendConfirm();
            return;
        }
        if ((key.KeyChar == 'd' || key.KeyChar == 'D') && _listFiles.Count > 0)
        {
            if (_locs[_tapeIdx] == StorageLocation.Suirdc) { ShowStatus("SUIRDC RECORDS ARE READ-ONLY", true); return; }
            _deleteTarget = _listFiles[_listIdx];
            _state        = MemState.DeleteConfirm;
            return;
        }
        if ((key.KeyChar == 'f' || key.KeyChar == 'F') && _tapeIdx < 2)
            StartDefrag(MemState.FileList);
    }

    private void HandleReadKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)    { _state = MemState.FileList; return; }
        if (key.Key == ConsoleKey.UpArrow)   { if (_readerScroll > 0) _readerScroll--; return; }
        if (key.Key == ConsoleKey.DownArrow) { _readerScroll++; return; }
    }

    private void HandleSendConfirmKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
        {
            _state = MemState.FileList;
            return;
        }
        if (key.KeyChar is 'y' or 'Y')
        {
            if (_listFiles.Count == 0 || _listIdx >= _listFiles.Count) { _state = MemState.FileList; return; }
            var file = _listFiles[_listIdx];

            if (!SendFits(file))
            {
                ShowStatus("TRANSFER REJECTED — INSUFFICIENT BLOCKS AT DESTINATION", true);
                _state = MemState.FileList;
                return;
            }

            _transferFileId   = file.Id;
            _transferDst      = _sendDst;
            _transferProgress = 0f;
            _transferMs       = Math.Clamp(600 + file.Blocks * 300, 1200, 4000);
            _transferStart    = _now;
            _state            = MemState.Transferring;
        }
    }

    private void HandleDeleteConfirmKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
        {
            _deleteTarget = null;
            _state        = MemState.FileList;
            return;
        }
        if (key.KeyChar is 'y' or 'Y')
        {
            if (_deleteTarget != null)
            {
                var loc = _locs[_tapeIdx];
                bool ok = loc == StorageLocation.Rover ? _repo.DeleteFromRover(_deleteTarget.Id)
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

    // ── Actions ───────────────────────────────────────────────────────────────

    private void RefreshFileList()
    {
        var loc     = _locs[_tapeIdx];
        _listFiles  = loc == StorageLocation.Suirdc ? _repo.GetSuirdcFiles() : _repo.GetFilesAt(loc);
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

    private void OpenSendConfirm()
    {
        var loc = _locs[_tapeIdx];
        if (loc == StorageLocation.Suirdc) { ShowStatus("SUIRDC RECORDS ARE READ-ONLY", true); return; }

        _sendDst = loc == StorageLocation.Rover ? StorageLocation.Local : StorageLocation.Suirdc;
        _state   = MemState.SendConfirm;
    }

    private bool SendFits(MemoryFile file)
    {
        if (_sendDst == StorageLocation.Suirdc)
            return _repo.GetSuirdcUsed() + file.Blocks <= MemoryRepository.SuirdcCapacity;
        return _repo.GetLayout(_sendDst).Count(b => b == null) >= file.Blocks;
    }

    private int SendFreeBlocks() =>
        _sendDst == StorageLocation.Suirdc
            ? MemoryRepository.SuirdcCapacity - _repo.GetSuirdcUsed()
            : _repo.GetLayout(_sendDst).Count(b => b == null);

    private void StartDefrag(MemState returnTo)
    {
        var loc    = _locs[_tapeIdx];
        var layout = _repo.GetLayout(loc);
        var target = _repo.GetDefragTarget(loc);

        if (layout.SequenceEqual(target)) { ShowStatus("STORAGE ALREADY COMPACTED", false); return; }

        _defragLoc    = loc;
        _defragCur    = new List<string?>(layout);
        _defragTarget = target;
        _defragTimer  = _now;
        _defragMoves  = 0;
        _defragTotal  = CountMismatches(_defragCur, _defragTarget);
        _defragReturn = returnTo;
        _state        = MemState.Defragging;
    }

    // One visible swap per tick. Converges because every swap fixes at least
    // the first mismatched position against the target layout.
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

    private static int CountMismatches(List<string?> a, List<string?> b)
    {
        int n = 0;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) n++;
        return n;
    }

    private void ShowStatus(string msg, bool error)
    {
        _statusMsg   = msg;
        _statusError = error;
        _statusTime  = _now;
    }

    // ── Render ────────────────────────────────────────────────────────────────

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
        int listSepRow  = listStart + ListVisible;
        int readerStart = listSepRow + 1;

        Ui.WriteDualTitleBorder(buffer, 0, panelTop, w,
            " MEMORY ALLOCATION SYSTEM ", $" {_progress.SolDisplay} ",
            ExoColors.ProksText, ExoColors.SignalText, ExoColors.ProksBorder);
        buffer.WriteAt(0, panelBottom, "└" + new string('─', innerW) + "┘", ExoColors.ProksBorder);
        for (int r = panelTop + 1; r < panelBottom; r++)
        {
            buffer.WriteAt(0,     r, "│", ExoColors.ProksBorder);
            buffer.WriteAt(w - 1, r, "│", ExoColors.ProksBorder);
        }

        buffer.WriteAt(0, tapeSepRow, "├" + new string('─', innerW) + "┤", ExoColors.ProksBorder);
        buffer.WriteAt(0, listSepRow, "├" + new string('─', innerW) + "┤", ExoColors.ProksBorder);

        var displayLayouts = new Dictionary<StorageLocation, List<string?>>
        {
            [StorageLocation.Rover] = _state == MemState.Defragging && _defragLoc == StorageLocation.Rover
                ? _defragCur : _repo.GetLayout(StorageLocation.Rover),
            [StorageLocation.Local] = _state == MemState.Defragging && _defragLoc == StorageLocation.Local
                ? _defragCur : _repo.GetLayout(StorageLocation.Local),
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
        else if (_state == MemState.Defragging)
            RenderDefragPanel(buffer, readerStart, panelBottom - 1, innerW);
        else if (_state == MemState.FileRead)
            RenderFileReader(buffer, readerStart, panelBottom - 1, innerW);
        else
            RenderFileListHint(buffer, readerStart, panelBottom - 1, innerW);

        RenderHints(buffer, hintsRow, innerW);
    }

    // ── tape section ──────────────────────────────────────────────────────────

    private void RenderTapeSection(IRenderBuffer buffer, int startRow, int innerW,
        Dictionary<StorageLocation, List<string?>> layouts)
    {
        int x   = 2;
        int row = startRow;

        for (int i = 0; i < 3; i++)
        {
            var    loc  = _locs[i];
            string name = _locNames[i];
            bool   sel  = _tapeIdx == i;

            int hRow = row;
            int tRow = row + 1;

            string marker    = sel ? (_blink.Visible ? "►" : "▷") : " ";
            string markerCol = sel
                ? (_blink.Visible ? ExoColors.PhosphorText : ExoColors.PhosphorDim)
                : ExoColors.ProksBorder;
            string nameCol = sel ? ExoColors.PhosphorText : ExoColors.ProksText;

            buffer.WriteAt(x, hRow, marker, markerCol);

            var files = loc == StorageLocation.Suirdc ? _repo.GetSuirdcFiles() : _repo.GetFilesAt(loc);

            if (loc == StorageLocation.Suirdc)
            {
                int used = _repo.GetSuirdcUsed();
                int cap  = MemoryRepository.SuirdcCapacity;

                int cx = x + 2;
                buffer.WriteAt(cx, hRow, name, nameCol);
                cx += name.Length + 2;

                string bar = BuildUsageBar(used, cap);
                buffer.WriteAt(cx, hRow, bar, ExoColors.ProksText);
                cx += bar.Length;

                var suirdcIds = files.Select(f => f.Id).ToHashSet();
                int queue     = _repo.GetFilesAt(StorageLocation.Local).Count(f => !suirdcIds.Contains(f.Id));

                string stats = $"   {used}/{cap} KB   QUEUE: {queue}";
                buffer.WriteAt(cx, hRow, stats, ExoColors.ProksDark);
                cx += stats.Length;

                buffer.WriteAt(cx, hRow, "   [READ-ONLY]", ExoColors.ProksPale);

                RenderSuirdcLine(buffer, x + 2, tRow, innerW - x - 3, files);

                row += 2;
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

                buffer.WriteAt(cx, hRow, stats, ExoColors.ProksDark);
                cx += stats.Length;

                if (frag > 0)
                    buffer.WriteAt(cx, hRow, $"   [!] FRAG {frag}%", ExoColors.FaultText);

                int rulerRow = row + 2;
                RenderTapeLine(buffer, x + 2, tRow, rulerRow, innerW - x - 3, layout, files);

                row += 3;
            }

            if (i < 2)
            {
                string arrow = i == 0 ? "↓  MOVE TO LOCAL" : "↓  SYNC TO SUIRDC";
                buffer.WriteAt(innerW / 2 - arrow.Length / 2, row, arrow, ExoColors.ProksDark);
                row++;
            }
        }
    }

    private void RenderSuirdcLine(IRenderBuffer buffer, int x, int y, int maxW, List<MemoryFile> files)
    {
        if (files.Count == 0)
        {
            buffer.WriteAt(x, y, "[ARCHIVE EMPTY]", ExoColors.ProksDark);
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
        boundaries.Add((x, 0));

        foreach (var (id, count) in runs)
        {
            if (curX >= x + maxW - 1) break;

            string seg;
            string col;

            if (id == null)
            {
                seg = "[" + new string('·', count * 2) + "]";
                col = ExoColors.ProksBorder;
            }
            else
            {
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

    private static void RenderTapeRuler(IRenderBuffer buffer, int x, int y, int maxW,
        List<(int xPos, int kb)> boundaries)
    {
        int lastLabelEndX = int.MinValue;

        for (int i = 0; i < boundaries.Count; i++)
        {
            var (bx, kb) = boundaries[i];
            string label = kb.ToString();
            int    rx    = bx - x;

            if (rx >= maxW) break;
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
        buffer.WriteAt(x + 25, hdrRow, "TYPE",   ExoColors.ProksDark);
        buffer.WriteAt(x + 31, hdrRow, "KB",     ExoColors.ProksDark);
        buffer.WriteAt(x + 36, hdrRow, "SOL",    ExoColors.ProksDark);
        buffer.WriteAt(x + 44, hdrRow, "STATUS", ExoColors.ProksDark);

        if (_statusMsg != null)
        {
            string statusCol = _statusError ? ExoColors.FaultText : ExoColors.PhosphorText;
            string truncated = Ui.Truncate(_statusMsg, innerW - 55);
            buffer.WriteAt(innerW - truncated.Length - 1, hdrRow, truncated, statusCol);
        }

        if (_listIdx < _listScroll)                _listScroll = _listIdx;
        if (_listIdx >= _listScroll + ListVisible) _listScroll = _listIdx - ListVisible + 1;
        _listScroll = Math.Max(0, _listScroll);

        bool inList = _state is MemState.FileList or MemState.FileRead
                               or MemState.SendConfirm or MemState.DeleteConfirm;

        var           loc       = _locs[_tapeIdx];
        List<string?> rowLayout = _repo.GetLayout(loc);

        for (int i = 0; i < ListVisible; i++)
        {
            int idx = _listScroll + i;
            if (idx >= _listFiles.Count) break;
            RenderFileRow(buffer, x, listStart + i, _listFiles[idx],
                idx == _listIdx && inList, innerW, rowLayout);
        }

        if (_listScroll > 0)
            buffer.WriteAt(innerW - 6, listStart, "▲ more", ExoColors.ProksDark);
        if (_listFiles.Count > _listScroll + ListVisible)
            buffer.WriteAt(innerW - 6, listStart + ListVisible - 1, "▼ more", ExoColors.ProksDark);
    }

    private void RenderFileRow(IRenderBuffer buffer, int x, int y,
        MemoryFile file, bool selected, int innerW, List<string?> layout)
    {
        var  loc  = _locs[_tapeIdx];
        bool frag = loc != StorageLocation.Suirdc && _repo.IsFragmented(file.Id, layout);

        string arrow    = selected ? (_blink.Visible ? "►" : "▷") : " ";
        string arrowCol = selected
            ? (_blink.Visible ? ExoColors.PhosphorText : ExoColors.PhosphorDim)
            : ExoColors.ProksBorder;

        // Fragmentation is a state, not a failure — only the STATUS cell warns.
        string rowCol = selected ? ExoColors.PhosphorText : ExoColors.ProksText;

        string name   = Ui.Truncate(file.DisplayName, 22).PadRight(22);
        string type   = file.Type.PadRight(5);
        string blocks = file.Blocks.ToString();
        string sol    = file.Sol.StartsWith("SOL ") ? file.Sol[4..] : file.Sol;

        buffer.WriteAt(x,      y, arrow,  arrowCol);
        buffer.WriteAt(x + 2,  y, name,   rowCol);

        buffer.WriteAt(x + 25, y, type,   ExoColors.ProksDark);
        buffer.WriteAt(x + 31, y, blocks, ExoColors.ProksDark);
        buffer.WriteAt(x + 36, y, sol,    ExoColors.ProksDark);

        if (frag)
            buffer.WriteAt(x + 44, y, "[!] FRAG", ExoColors.FaultText);
        else
            buffer.WriteAt(x + 44, y, "OK", ExoColors.ProksDark);
    }

    // ── send confirm panel ────────────────────────────────────────────────────

    private void RenderSendConfirmPanel(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        if (_listFiles.Count == 0 || _listIdx >= _listFiles.Count) return;

        var    file      = _listFiles[_listIdx];
        int    x         = 2;
        bool   toArchive = _sendDst == StorageLocation.Suirdc;
        string dstName   = _locNames[(int)_sendDst];
        bool   fits      = SendFits(file);
        int    free      = SendFreeBlocks();

        Ui.WriteTransmitBorder(buffer, 0, startRow, innerW, "─ CONFIRM TRANSFER ");

        int row = startRow + 1;
        buffer.WriteAt(x, row++, $"  {file.DisplayName} [{file.Type}]  —  {file.Blocks} KB",
            ExoColors.PhosphorText);
        buffer.WriteAt(x, row++, new string('─', Math.Min(56, innerW - x)), ExoColors.ProksBorder);

        buffer.WriteAt(x, row, $"  DESTINATION: {dstName}", ExoColors.ProksText);
        string freeInfo = $"DST FREE: {free} KB" + (fits ? "" : "  — INSUFFICIENT");
        buffer.WriteAt(x + 34, row++, freeInfo, fits ? ExoColors.ProksDark : ExoColors.FaultText);

        string note = toArchive
            ? "  RECORD WILL BE SURRENDERED TO SUIRDC — NO RETRIEVAL."
            : "  NOTE: FILE WILL BE REMOVED FROM SOURCE.";
        buffer.WriteAt(x, row++, note, ExoColors.FaultText);
        buffer.WriteAt(x, row++, new string('─', Math.Min(56, innerW - x)), ExoColors.ProksBorder);

        if (row <= endRow)
        {
            if (fits) buffer.WriteAt(x, row++, "  [Y]  CONFIRM TRANSFER", ExoColors.PhosphorBright);
            else      buffer.WriteAt(x, row++, "  [Y]  CONFIRM TRANSFER — UNAVAILABLE", ExoColors.ProksDark);
        }
        if (row <= endRow)
            buffer.WriteAt(x, row, "  [N]  CANCEL", ExoColors.PhosphorText);
    }

    // ── delete confirm panel ──────────────────────────────────────────────────

    private void RenderDeleteConfirmPanel(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        if (_deleteTarget == null) return;
        int midRow  = startRow + (endRow - startRow) / 2;
        int sepW    = Math.Min(44, innerW - 4);
        int usableW = innerW - 2;
        int cx(int len) => 2 + Math.Max(0, (usableW - len) / 2);

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

        int    x       = 2;
        string srcName = _repo.GetFile(_transferFileId)?.DisplayName ?? _transferFileId.ToUpper();
        string dstName = _locNames[(int)_transferDst];

        int    arrowSuffix = 2 + dstName.Length;
        int    dashMax     = Math.Max(0, innerW - arrowSuffix - 4);
        int    dashes      = (int)(_transferProgress * dashMax);
        string arrowLine   = "  " + new string('─', dashes) + "► " + dstName;

        Ui.WriteTransmitBorder(buffer, 0, startRow, innerW, "─ TRANSMITTING ");
        buffer.WriteAt(x, startRow + 1,
            Ui.Truncate($"  {srcName}", innerW).PadRight(innerW), ExoColors.PhosphorText);
        buffer.WriteAt(x, startRow + 2,
            arrowLine.PadRight(Math.Min(innerW, arrowLine.Length + 1)), ExoColors.ProksPale);
        if (startRow + 3 <= endRow)
            Ui.WriteTransmitBorder(buffer, 0, startRow + 3, innerW, "");
    }

    // ── defrag panel ──────────────────────────────────────────────────────────

    private void RenderDefragPanel(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        if (endRow - startRow < 3) return;

        int    x       = 2;
        string locName = _locNames[(int)_defragLoc];

        int   remaining = CountMismatches(_defragCur, _defragTarget);
        float progress  = _defragTotal == 0 ? 1f : 1f - (float)remaining / _defragTotal;
        const int barW  = 24;
        int   fill      = Math.Clamp((int)(progress * barW), 0, barW);
        string bar      = "[" + new string('─', fill) + new string('·', barW - fill) + "]";

        Ui.WriteTransmitBorder(buffer, 0, startRow, innerW, "─ DEFRAGMENTATION ");
        buffer.WriteAt(x, startRow + 1, $"  {locName} — COMPACTING STORAGE", ExoColors.PhosphorText);
        buffer.WriteAt(x, startRow + 2, "  " + bar, ExoColors.ProksPale);
        buffer.WriteAt(x + 4 + barW, startRow + 2, $"  {_defragMoves} BLOCKS RELOCATED", ExoColors.ProksDark);
        if (startRow + 3 <= endRow)
            Ui.WriteTransmitBorder(buffer, 0, startRow + 3, innerW, "");
    }

    // ── reader area placeholders ──────────────────────────────────────────────

    private static void RenderFileListHint(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        int midRow = startRow + (endRow - startRow) / 2;
        int x      = 2;
        string msg = "AWAITING READ COMMAND";
        buffer.WriteAt(x + (innerW - x - msg.Length) / 2, midRow, msg, ExoColors.ProksDark);
    }

    private static void RenderTapeSelectReader(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        int midRow = startRow + (endRow - startRow) / 2;
        int x      = 2;

        string msg = "SELECT STORAGE MEDIUM";
        string sub = "↑↓  NAVIGATE     ENTER  OPEN FILES";

        buffer.WriteAt(x + (innerW - x - msg.Length) / 2, midRow - 1, msg, ExoColors.ProksText);
        buffer.WriteAt(x + (innerW - x - sub.Length) / 2, midRow + 1, sub, ExoColors.ProksDark);
    }

    // ── file reader ───────────────────────────────────────────────────────────

    private void RenderFileReader(IRenderBuffer buffer, int startRow, int endRow, int innerW)
    {
        if (_listFiles.Count == 0 || _listIdx >= _listFiles.Count) return;
        var file = _listFiles[_listIdx];

        int x = 2;

        string titleLine = Ui.Truncate($"{file.DisplayName} [{file.Type}]  —  {file.Description}", innerW - x - 12);
        buffer.WriteAt(x, startRow, titleLine, ExoColors.PhosphorText);
        buffer.WriteAt(innerW - 9, startRow, "[READING]", ExoColors.PhosphorDim);

        buffer.WriteAt(x, startRow + 1,
            new string('─', Math.Min(innerW - x, 60)), ExoColors.ProksBorder);

        int contentTop = startRow + 2;
        int maxRows    = endRow - contentTop + 1;
        if (maxRows <= 0) return;

        int maxScroll = Math.Max(0, _readerLines.Count - maxRows);
        _readerScroll = Math.Clamp(_readerScroll, 0, maxScroll);

        bool hasAbove = _readerScroll > 0;
        bool hasBelow = _readerScroll + maxRows < _readerLines.Count;

        for (int i = 0; i < maxRows; i++)
        {
            int li = _readerScroll + i;
            if (li >= _readerLines.Count) break;
            int row = contentTop + i;

            string line = Ui.Truncate(_readerLines[li], innerW - x - 8);
            buffer.WriteAt(x, row, line, ExoColors.ProksText);

            if (hasAbove && i == 0)
                buffer.WriteAt(innerW - 7, row, "▲ more", ExoColors.ProksDark);
            else if (hasBelow && i == maxRows - 1)
                buffer.WriteAt(innerW - 7, row, "▼ more", ExoColors.ProksDark);
        }

        if (_readerLines.Count > 0)
        {
            int    page  = _readerScroll / Math.Max(1, maxRows) + 1;
            int    total = (_readerLines.Count - 1) / Math.Max(1, maxRows) + 1;
            string ind   = $"↕ {page}/{total}";
            buffer.WriteAt(innerW - ind.Length - 1, endRow, ind, ExoColors.ProksDark);
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
            MemState.SendConfirm   => "Y  confirm transfer     N / ESC  cancel",
            MemState.DeleteConfirm => "Y  confirm delete     N / ESC  cancel",
            MemState.Transferring  => "TRANSFER IN PROGRESS — PLEASE WAIT",
            MemState.Defragging    => "DEFRAGMENTING — PLEASE WAIT",
            _                      => ""
        };
        if (hints.Length == 0) return;
        int hintX = Math.Max(0, (innerW - hints.Length) / 2);
        buffer.WriteAt(hintX, y, hints, ExoColors.ProksDark);
    }
}
