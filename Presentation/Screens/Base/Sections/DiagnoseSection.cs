using ExoProxy.Core;
using ExoProxy.Data;

namespace ExoProxy.Presentation.Screens.Base.Sections;

public sealed class DiagnoseSection : IBaseSection
{
    private enum DiagnoseMode
    {
        Browse,
        ActionMenu
    }

    public string SectionId => SectionIds.Diag;

    public BaseSectionResponse Response { get; private set; }
        = new(BaseSectionRequest.Stay, null);

    private readonly string _operatorLogin;
    private readonly RoverStats _rover;
    private readonly List<RoverElectronics> _electronics;
    private TimeSpan? _scanStartedAt;
    private TimeSpan? _revealStartedAt;
    private TimeSpan _now;
    private float _scanProgress;
    private DiagnoseMode _mode = DiagnoseMode.Browse;
    private int _selectedIndex;
    private int _selectedAction;

    private const int BoxWidth = 72;
    private const int BoxInner = BoxWidth - 2;
    private const int BoxTop = 2;
    private const int ScanDurationMs = 2400;
    private const int ScanBarWidth = 44;
    private const int RevealLineMs = 120;

    public DiagnoseSection(string operatorLogin, RoverStats rover)
    {
        _operatorLogin = operatorLogin;
        _rover = rover;
        _electronics = RoverElectronics.Load(_operatorLogin, _rover.RoverTotalResistance);
    }

    public void BeginScan(TimeSpan now)
    {
        RoverElectronicsFailureChooser.ApplyPendingFailures(_rover, _electronics);
        RoverElectronics.Save(_operatorLogin, _electronics);
        _rover.Save();
        _scanStartedAt = now;
        _revealStartedAt = null;
        _scanProgress = 0f;
        _mode = DiagnoseMode.Browse;
        _selectedAction = 0;
    }

    public void Update(GameTime time, InputEvent? input)
    {
        _now = time.Total;
        Response = new(BaseSectionRequest.Stay, null);

        if (input?.Key.Key == ConsoleKey.Escape)
        {
            if (_mode == DiagnoseMode.ActionMenu)
            {
                _mode = DiagnoseMode.Browse;
                _selectedAction = 0;
                return;
            }

            _scanStartedAt = null;
            _revealStartedAt = null;
            Response = new(BaseSectionRequest.GoToHub, null);
            return;
        }

        if (input?.Key.Key == ConsoleKey.F4)
        {
            _scanStartedAt = null;
            _revealStartedAt = null;
            _scanProgress = 1f;
            return;
        }

        if (_scanStartedAt is not null)
        {
            float elapsedMs = (float)(time.Total - _scanStartedAt.Value).TotalMilliseconds;
            _scanProgress = Math.Clamp(elapsedMs / ScanDurationMs, 0f, 1f);

            if (_scanProgress >= 1f)
            {
                _scanStartedAt = null;
                _revealStartedAt ??= time.Total;
            }
        }

        if (_revealStartedAt is not null &&
            GetVisibleLineCount() >= GetTotalDiagnosticLines())
        {
            _revealStartedAt = null;
            return;
        }

        if (_scanStartedAt is not null || _revealStartedAt is not null || input is null)
            return;

        HandleBrowseInput(input.Value.Key);
    }

    public void Render(IRenderBuffer buffer)
    {
        if (_scanStartedAt is not null)
        {
            RenderScan(buffer);
            return;
        }

        int visibleLines = GetVisibleLineCount();
        RenderDiagnosticTable(buffer, visibleLines);

        if (_mode == DiagnoseMode.ActionMenu)
            RenderActionMenu(buffer);
    }

    private void RenderDiagnosticTable(IRenderBuffer buffer, int visibleLines)
    {
        int left = (buffer.Width - BoxWidth) / 2;
        int row = BoxTop;

        if (!TryRenderLine(buffer, visibleLines, 1, left, row++, "+" + new string('-', BoxInner) + "+", ExoColors.ProksBorder))
            return;

        if (!TryRenderDiagnosticTitle(buffer, visibleLines, 2, left, row++))
            return;

        if (!TryRenderLine(buffer, visibleLines, 3, left, row++, "+" + new string('-', BoxInner) + "+", ExoColors.ProksBorder))
            return;

        if (!TryRenderRow(buffer, visibleLines, 4, left, row++, "ID", "COMPONENT", "OHM", ExoColors.ProksText))
            return;

        if (!TryRenderLine(buffer, visibleLines, 5, left, row++, "|" + new string('-', BoxInner) + "|", ExoColors.ProksBorder))
            return;

        int lineIndex = 6;
        for (int i = 0; i < _electronics.Count; i++)
        {
            var item = _electronics[i];
            bool selected = _mode == DiagnoseMode.Browse && _selectedIndex == i;
            string id = selected ? ">" + item.ID.ToString("0") : item.ID.ToString("00");
            string color = item.IsDamaged
                ? ExoColors.FaultText
                : selected
                    ? ExoColors.PhosphorBright
                    : ExoColors.ProksPale;

            if (!TryRenderRow(
                    buffer,
                    visibleLines,
                    lineIndex++,
                    left,
                    row++,
                    id,
                    item.Name,
                    item.DisplayedResistance.ToString(),
                    color))
                return;
        }

        if (!TryRenderLine(buffer, visibleLines, lineIndex++, left, row++, "|" + new string('-', BoxInner) + "|", ExoColors.ProksBorder))
            return;

        if (!TryRenderRow(buffer, visibleLines, lineIndex++, left, row++, "", "RoverTotalResistance", _rover.RoverTotalResistance.ToString(), ExoColors.PhosphorText))
            return;

        if (!TryRenderRow(buffer, visibleLines, lineIndex++, left, row++, "", "Electronics Resistance Sum", _electronics.Sum(e => e.DisplayedResistance).ToString(), ExoColors.ProksText))
            return;

        if (!TryRenderLine(buffer, visibleLines, lineIndex++, left, row++, "+" + new string('-', BoxInner) + "+", ExoColors.ProksBorder))
            return;

        string hint = _mode == DiagnoseMode.Browse
            ? "UP/DOWN Select  ENTER Actions  ESC Back"
            : "UP/DOWN Select  ENTER Confirm  ESC Cancel";
        TryRenderText(
            buffer,
            visibleLines,
            lineIndex,
            Math.Max(left + 2, left + BoxWidth - hint.Length - 2),
            row + 1,
            hint,
            ExoColors.ProksPale);
    }

    private void HandleBrowseInput(ConsoleKeyInfo key)
    {
        if (_mode == DiagnoseMode.Browse)
        {
            if (key.Key == ConsoleKey.UpArrow)
            {
                if (_selectedIndex > 0)
                    _selectedIndex--;
                return;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                if (_selectedIndex < _electronics.Count - 1)
                    _selectedIndex++;
                return;
            }

            if (key.Key == ConsoleKey.Enter && _electronics.Count > 0)
            {
                _mode = DiagnoseMode.ActionMenu;
                _selectedAction = 0;
            }

            return;
        }

        if (key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow)
        {
            _selectedAction = _selectedAction == 0 ? 1 : 0;
            return;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            ApplySelectedAction();
            _mode = DiagnoseMode.Browse;
            _selectedAction = 0;
        }
    }

    private void ApplySelectedAction()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _electronics.Count)
            return;

        var item = _electronics[_selectedIndex];
        int healthStep = Math.Max(1, (int)Math.Round(_rover.MaxHealth * 0.1));

        if (_selectedAction == 0)
        {
            if (item.IsShortCircuited)
            {
                item.IsDamaged = false;
                _rover.CurrentHealth = Math.Min(_rover.MaxHealth, _rover.CurrentHealth + healthStep);
            }
            else if (!item.IsDamaged)
            {
                item.IsOpenCircuit = true;
                _rover.CurrentHealth = Math.Max(0, _rover.CurrentHealth - healthStep);
            }
        }
        else
        {
            if (item.IsOpenCircuit)
            {
                item.IsDamaged = false;
                _rover.CurrentHealth = Math.Min(_rover.MaxHealth, _rover.CurrentHealth + healthStep);
            }
            else if (!item.IsDamaged)
            {
                item.IsShortCircuited = true;
                _rover.CurrentHealth = Math.Max(0, _rover.CurrentHealth - healthStep);
            }
        }

        _rover.ProcessedDamageThresholds = RoverElectronicsFailureChooser.GetDamageThresholdCount(_rover);
        RoverElectronics.Save(_operatorLogin, _electronics);
        _rover.Save();
    }

    private void RenderActionMenu(IRenderBuffer buffer)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _electronics.Count)
            return;

        var item = _electronics[_selectedIndex];
        const int modalWidth = 38;
        const int modalInner = modalWidth - 2;
        int left = (buffer.Width - modalWidth) / 2;
        int top = Math.Max(8, buffer.Height / 2 - 3);

        buffer.WriteAt(left, top++, "+" + new string('-', modalInner) + "+", ExoColors.ProksBorder);
        buffer.WriteAt(left, top, "|", ExoColors.ProksBorder);
        buffer.WriteAt(left + 2, top, $"LINE CONTROL - ID {item.ID:00}", ExoColors.PhosphorText);
        buffer.WriteAt(left + modalWidth - 1, top++, "|", ExoColors.ProksBorder);
        buffer.WriteAt(left, top++, "|" + new string('-', modalInner) + "|", ExoColors.ProksBorder);

        RenderActionRow(buffer, left, top++, 0, $"OPEN LINE ID {item.ID:00}");
        RenderActionRow(buffer, left, top++, 1, $"CLOSE LINE ID {item.ID:00}");

        buffer.WriteAt(left, top++, "+" + new string('-', modalInner) + "+", ExoColors.ProksBorder);
    }

    private void RenderActionRow(IRenderBuffer buffer, int left, int row, int actionIndex, string label)
    {
        const int modalWidth = 38;
        const int modalInner = modalWidth - 2;
        bool selected = _selectedAction == actionIndex;
        string prefix = selected ? "> " : "  ";
        string color = selected ? ExoColors.PhosphorBright : ExoColors.ProksPale;

        buffer.WriteAt(left, row, "|", ExoColors.ProksBorder);
        buffer.WriteAt(left + 1, row, (prefix + label).PadRight(modalInner), color);
        buffer.WriteAt(left + modalWidth - 1, row, "|", ExoColors.ProksBorder);
    }

    private static void WriteRow(
        IRenderBuffer buffer,
        int left,
        int row,
        string id,
        string component,
        string resistance,
        string color)
    {
        string line = $" {id,-4} {component,-47} {resistance,6} ";

        buffer.WriteAt(left, row, "|", ExoColors.ProksBorder);
        buffer.WriteAt(left + 1, row, line.PadRight(BoxInner), color);
        buffer.WriteAt(left + BoxWidth - 1, row, "|", ExoColors.ProksBorder);
    }

    private int GetVisibleLineCount()
    {
        int totalLines = GetTotalDiagnosticLines();

        if (_revealStartedAt is null)
            return totalLines;

        double elapsedMs = (_now - _revealStartedAt.Value).TotalMilliseconds;
        int visibleLines = 1 + (int)(elapsedMs / RevealLineMs);
        return Math.Clamp(visibleLines, 1, totalLines);
    }

    private int GetTotalDiagnosticLines() => 10 + _electronics.Count;

    private void RenderScan(IRenderBuffer buffer)
    {
        int left = (buffer.Width - BoxWidth) / 2;
        int row = Math.Max(2, buffer.Height / 2 - 4);
        int barFill = Math.Clamp((int)Math.Round(_scanProgress * ScanBarWidth), 0, ScanBarWidth);

        string title = "INITIALIZING ROVER ELECTRONICS DIAGNOSTICS";
        string status = GetScanStatus(_scanProgress);
        string bar = Ui.LoadingBar(barFill, ScanBarWidth);
        string percent = $"{(int)Math.Round(_scanProgress * 100),3}%";

        buffer.WriteAt(left, row++, "+" + new string('-', BoxInner) + "+", ExoColors.ProksBorder);
        buffer.WriteAt(left, row, "|", ExoColors.ProksBorder);
        buffer.WriteAt(left + 3, row, title, ExoColors.PhosphorText);
        buffer.WriteAt(left + BoxWidth - 1, row++, "|", ExoColors.ProksBorder);
        buffer.WriteAt(left, row++, "|" + new string('-', BoxInner) + "|", ExoColors.ProksBorder);

        buffer.WriteAt(left, row, "|", ExoColors.ProksBorder);
        buffer.WriteAt(left + 3, row, status.PadRight(BoxInner - 4), ExoColors.ProksPale);
        buffer.WriteAt(left + BoxWidth - 1, row++, "|", ExoColors.ProksBorder);

        buffer.WriteAt(left, row, "|", ExoColors.ProksBorder);
        buffer.WriteAt(left + 3, row, bar, ExoColors.PhosphorText);
        buffer.WriteAt(left + 5 + ScanBarWidth, row, percent, ExoColors.SignalText);
        buffer.WriteAt(left + BoxWidth - 1, row++, "|", ExoColors.ProksBorder);

        buffer.WriteAt(left, row++, "+" + new string('-', BoxInner) + "+", ExoColors.ProksBorder);
    }

    private bool TryRenderDiagnosticTitle(IRenderBuffer buffer, int visibleLines, int lineIndex, int left, int row)
    {
        if (visibleLines < lineIndex)
            return false;

        buffer.WriteAt(left, row, "|", ExoColors.ProksBorder);
        buffer.WriteAt(left + 3, row, "ROVER ELECTRONICS DIAGNOSTICS", ExoColors.PhosphorText);
        buffer.WriteAt(left + BoxWidth - 1, row, "|", ExoColors.ProksBorder);
        return true;
    }

    private bool TryRenderRow(
        IRenderBuffer buffer,
        int visibleLines,
        int lineIndex,
        int left,
        int row,
        string id,
        string component,
        string resistance,
        string color)
    {
        if (visibleLines < lineIndex)
            return false;

        WriteRow(buffer, left, row, id, component, resistance, color);
        return true;
    }

    private bool TryRenderLine(
        IRenderBuffer buffer,
        int visibleLines,
        int lineIndex,
        int left,
        int row,
        string text,
        string color)
    {
        if (visibleLines < lineIndex)
            return false;

        buffer.WriteAt(left, row, text, color);
        return true;
    }

    private void TryRenderText(
        IRenderBuffer buffer,
        int visibleLines,
        int lineIndex,
        int left,
        int row,
        string text,
        string color)
    {
        if (visibleLines < lineIndex)
            return;

        buffer.WriteAt(left, row, text, color);
    }

    private static string GetScanStatus(float progress)
    {
        if (progress < 0.25f) return "LINKING SENSOR BUS...";
        if (progress < 0.50f) return "SAMPLING RESISTANCE VALUES...";
        if (progress < 0.75f) return "CHECKING COMPONENT SIGNATURES...";
        return "COMPILING DIAGNOSTIC TABLE...";
    }
}
