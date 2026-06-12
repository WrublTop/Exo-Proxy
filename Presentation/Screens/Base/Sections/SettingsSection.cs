using ExoProxy.Core;
using ExoProxy.Data;

namespace ExoProxy.Presentation.Screens.Base.Sections;

public sealed class SettingsSection : IBaseSection
{
    public string SectionId => SectionIds.Settings;
    public BaseSectionResponse Response { get; private set; } = new(BaseSectionRequest.Stay, null);

    private readonly GameSettings _settings;
    private readonly OperatorAccount _account;
    private readonly OperatorRegistry _registry;

    private enum Mode { Browse, NewPassword, ConfirmPassword, ConfirmReset }
    private Mode _mode = Mode.Browse;

    private string _input = "";
    private string _newPasswordBuffer = "";
    private string _message = "";
    private bool _messageIsError;
    private BlinkState _blink;

    private const int BoxWidth = 64;
    private const int BoxInner = BoxWidth - 2;
    private const int BoxTop   = 2;

    private sealed class Item
    {
        public string Label       { get; init; } = "";
        public bool IsHeader      { get; init; }
        public bool IsPlaceholder { get; init; }
        public bool IsAction      { get; init; }
        public bool IsBoolean     { get; init; }
        public string[]? Values   { get; init; }
        public int ValueIndex     { get; set; }
        public string? Key        { get; init; }
    }

    private readonly List<Item> _items;
    private readonly List<int> _selectableIndices;
    private int _selectedNavIndex;

    public SettingsSection(GameSettings settings, OperatorAccount account, OperatorRegistry registry)
    {
        _settings   = settings;
        _account    = account;
        _registry   = registry;

        _items = BuildItems();
        _selectableIndices = _items
            .Select((item, idx) => (item, idx))
            .Where(x => !x.item.IsHeader)
            .Select(x => x.idx)
            .ToList();
    }

    private List<Item> BuildItems()
    {
        static int Idx(string[] arr, string val) => Math.Max(0, Array.IndexOf(arr, val));

        string[] themes    = ["AMBER", "GREEN"];
        string[] bright    = ["DIM", "NORMAL", "BRIGHT"];
        string[] contrast  = ["LOW", "MEDIUM", "HIGH"];
        string[] languages = ["ENGLISH", "POLSKI"];
        string[] speeds    = ["SLOW", "NORMAL", "FAST", "INSTANT"];

        return
        [
            new() { Label = "AUDIO",          IsHeader = true },
            new() { Label = "Main volume",    IsPlaceholder = true },
            new() { Label = "Music volume",   IsPlaceholder = true },
            new() { Label = "Effects volume", IsPlaceholder = true },
            new() { Label = "Ambient volume", IsPlaceholder = true },
            new() { Label = "",               IsHeader = true },
            new() { Label = "DISPLAY",        IsHeader = true },
            new() { Label = "Theme",              Values = themes,    ValueIndex = Idx(themes,    _settings.Theme),           Key = "theme" },
            new() { Label = "Brightness",         Values = bright,    ValueIndex = Idx(bright,    _settings.Brightness),      Key = "brightness" },
            new() { Label = "Contrast",           Values = contrast,  ValueIndex = Idx(contrast,  _settings.Contrast),        Key = "contrast" },
            new() { Label = "Animations",         IsBoolean = true,   ValueIndex = _settings.Animations ? 0 : 1,              Key = "animations" },
            new() { Label = "",               IsHeader = true },
            new() { Label = "SYSTEM",         IsHeader = true },
            new() { Label = "Language",           Values = languages, ValueIndex = Idx(languages, _settings.Language),        Key = "language" },
            new() { Label = "Typewriter speed",   Values = speeds,    ValueIndex = Idx(speeds,    _settings.TypewriterSpeed), Key = "typewriter" },
            new() { Label = "",               IsHeader = true },
            new() { Label = "ACCOUNT",        IsHeader = true },
            new() { Label = "Change password", IsAction = true, Key = "change_password" },
            new() { Label = "Reset all data",  IsAction = true, Key = "reset_data" },
        ];
    }

    private Item SelectedItem => _items[_selectableIndices[_selectedNavIndex]];

    public void Update(GameTime time, InputEvent? input)
    {
        var now = time.Total;
        _blink.Update(now);

        Response = new(BaseSectionRequest.Stay, null);

        if (input is null) return;

        var key = input.Value.Key;

        switch (_mode)
        {
            case Mode.Browse:          UpdateBrowse(key);          break;
            case Mode.NewPassword:     UpdateNewPassword(key);     break;
            case Mode.ConfirmPassword: UpdateConfirmPassword(key); break;
            case Mode.ConfirmReset:    UpdateConfirmReset(key);    break;
        }
    }

    private void UpdateBrowse(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            Response = new(BaseSectionRequest.GoToHub, null);
            return;
        }
        if (key.Key == ConsoleKey.UpArrow)
        {
            if (_selectedNavIndex > 0) _selectedNavIndex--;
            _message = "";
            return;
        }
        if (key.Key == ConsoleKey.DownArrow)
        {
            if (_selectedNavIndex < _selectableIndices.Count - 1) _selectedNavIndex++;
            _message = "";
            return;
        }

        var item = SelectedItem;

        if ((key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow) && !item.IsPlaceholder)
        {
            if (item.IsBoolean)
            {
                item.ValueIndex = item.ValueIndex == 0 ? 1 : 0;
                ApplySetting(item);
            }
            else if (item.Values is not null)
            {
                int dir = key.Key == ConsoleKey.RightArrow ? 1 : -1;
                item.ValueIndex = (item.ValueIndex + dir + item.Values.Length) % item.Values.Length;
                ApplySetting(item);
            }
            return;
        }

        if (key.Key == ConsoleKey.Enter && item.IsAction)
        {
            switch (item.Key)
            {
                case "change_password":
                    _input = "";
                    _message = "";
                    _mode = Mode.NewPassword;
                    break;
                case "reset_data":
                    _message = "";
                    _mode = Mode.ConfirmReset;
                    break;
            }
        }
    }

    private void UpdateNewPassword(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape) { _input = ""; _message = ""; _mode = Mode.Browse; return; }
        if (key.Key == ConsoleKey.Backspace) { if (_input.Length > 0) _input = _input[..^1]; return; }
        if (key.Key == ConsoleKey.Enter)
        {
            if (_input.Length == 0) return;
            _newPasswordBuffer = _input;
            _input = "";
            _message = "";
            _mode = Mode.ConfirmPassword;
            return;
        }
        if (key.KeyChar != '\0' && _input.Length < 24) _input += key.KeyChar;
    }

    private void UpdateConfirmPassword(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape) { _input = ""; _newPasswordBuffer = ""; _message = ""; _mode = Mode.Browse; return; }
        if (key.Key == ConsoleKey.Backspace) { if (_input.Length > 0) _input = _input[..^1]; return; }
        if (key.Key == ConsoleKey.Enter)
        {
            if (_input != _newPasswordBuffer)
            {
                _message = "Passwords do not match.";
                _messageIsError = true;
                _input = "";
                _newPasswordBuffer = "";
                _mode = Mode.NewPassword;
                return;
            }
            var acc = _registry.Accounts.FirstOrDefault(a => a.Login == _account.Login);
            if (acc is not null) { acc.PasswordHash = PasswordHasher.Hash(_input); _registry.Save(); }
            _input = ""; _newPasswordBuffer = "";
            _message = "Password changed."; _messageIsError = false;
            _mode = Mode.Browse;
            return;
        }
        if (key.KeyChar != '\0' && _input.Length < 24) _input += key.KeyChar;
    }

    private void UpdateConfirmReset(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape || key.KeyChar == 'n' || key.KeyChar == 'N')
        {
            _mode = Mode.Browse;
            return;
        }
        if (key.KeyChar == 'y' || key.KeyChar == 'Y')
        {
            _registry.ResetToDefaults();
            // Exit through the session pipeline — Environment.Exit would skip
            // the finally block in Program.cs and leave the terminal broken.
            Response = new(BaseSectionRequest.ExitGame, null);
        }
    }

    private void ApplySetting(Item item)
    {
        string val = item.IsBoolean
            ? (item.ValueIndex == 0 ? "ON" : "OFF")
            : item.Values![item.ValueIndex];

        switch (item.Key)
        {
            case "theme":
                _settings.Theme = val;
                ExoColors.Apply(_settings.Theme, _settings.Brightness);
                break;
            case "brightness":
                _settings.Brightness = val;
                ExoColors.Apply(_settings.Theme, _settings.Brightness);
                break;
            case "contrast":    _settings.Contrast        = val; break; // TODO: unwired — implement with gameplay UI pass
            case "animations":  _settings.Animations      = val == "ON"; break; // TODO: unwired — should gate boot/transfer animations
            case "language":    _settings.Language        = val; break; // TODO: unwired — requires a string table
            case "typewriter":  _settings.TypewriterSpeed = val; break; // TODO: unwired — no consumer yet
        }
        _settings.Save();
    }

    public void Render(IRenderBuffer buffer)
    {
        int left = (buffer.Width - BoxWidth) / 2;

        if (_mode is Mode.NewPassword or Mode.ConfirmPassword) { RenderPasswordChange(buffer, left); return; }
        if (_mode == Mode.ConfirmReset) { RenderConfirmReset(buffer, left); return; }

        // ── Browse ────────────────────────────────────────────────────────────
        int selectedIdx = _selectableIndices[_selectedNavIndex];
        int boxBottom   = BoxTop + _items.Count + 3; // +2 for top/bottom padding rows

        // Top border with inline title
        const string titleLabel = " SETTINGS ";
        buffer.WriteAt(left,                               BoxTop, "┌──",                                              ExoColors.ProksBorder);
        buffer.WriteAt(left + 3,                           BoxTop, titleLabel,                                         ExoColors.ProksText);
        buffer.WriteAt(left + 3 + titleLabel.Length,       BoxTop, new string('─', BoxInner - 2 - titleLabel.Length) + "┐", ExoColors.ProksBorder);

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            int row  = BoxTop + 2 + i;
            bool sel = i == selectedIdx;

            buffer.WriteAt(left,               row, "│", ExoColors.ProksBorder);
            buffer.WriteAt(left + BoxWidth - 1, row, "│", ExoColors.ProksBorder);

            if (item.IsHeader)
            {
                if (!string.IsNullOrEmpty(item.Label))
                {
                    const string dashPrefix = "─── ";
                    string fillRight = " " + new string('─', BoxInner - dashPrefix.Length - 1 - item.Label.Length);
                    buffer.WriteAt(left,                                                row, "├",          ExoColors.ProksBorder);
                    buffer.WriteAt(left + 1,                                            row, dashPrefix,   ExoColors.ProksBorder);
                    buffer.WriteAt(left + 1 + dashPrefix.Length,                        row, item.Label,   ExoColors.ProksText);
                    buffer.WriteAt(left + 1 + dashPrefix.Length + item.Label.Length,    row, fillRight,    ExoColors.ProksBorder);
                    buffer.WriteAt(left + BoxWidth - 1,                                 row, "┤",          ExoColors.ProksBorder);
                }
                continue;
            }

            // selection indicator
            if (sel) buffer.WriteAt(left + 2, row, _blink.Visible ? "►" : "▷",
                _blink.Visible ? ExoColors.PhosphorText : ExoColors.PhosphorDim);

            string labelColor = sel ? ExoColors.PhosphorText : ExoColors.ProksPale;
            buffer.WriteAt(left + 4, row, item.Label, labelColor);

            // value rendering
            if (item.IsPlaceholder)
            {
                const string bar = "[────────────────]  N/A";
                buffer.WriteAt(left + BoxInner - bar.Length, row, bar, ExoColors.ProksDark);
            }
            else if (item.IsBoolean)
            {
                bool isOn  = item.ValueIndex == 0;
                string onPart  = isOn  ? "■ ON" : "□ ON";
                string offPart = !isOn ? "■ OFF" : "□ OFF";
                string onColor  = isOn  ? (sel ? ExoColors.PhosphorText : ExoColors.ProksText)
                                        : ExoColors.ProksDark;
                string offColor = !isOn ? ExoColors.FaultText : ExoColors.ProksDark;
                int valX = left + BoxInner - 12;
                buffer.WriteAt(valX,     row, onPart,  onColor);
                buffer.WriteAt(valX + 6, row, offPart, offColor);
            }
            else if (item.IsAction)
            {
                if (sel) buffer.WriteAt(left + BoxInner - 7, row, "[ENTER]", ExoColors.ProksPale);
            }
            else if (item.Values is not null)
            {
                string val        = item.Values[item.ValueIndex];
                string arrowColor = sel ? ExoColors.PhosphorText : ExoColors.ProksPale;
                string valColor   = sel ? ExoColors.PhosphorText : ExoColors.ProksPale;
                int valX = left + BoxInner - val.Length - 4;
                buffer.WriteAt(valX,                  row, "◄ ",  arrowColor);
                buffer.WriteAt(valX + 2,              row, val,   valColor);
                buffer.WriteAt(valX + 2 + val.Length, row, " ►",  arrowColor);
            }
        }

        buffer.WriteAt(left, boxBottom, "└" + new string('─', BoxInner) + "┘", ExoColors.ProksBorder);

        if (!string.IsNullOrEmpty(_message))
        {
            string color = _messageIsError ? ExoColors.FaultText : ExoColors.ProksPale;
            buffer.WriteAt((buffer.Width - _message.Length) / 2, boxBottom + 1, _message, color);
        }

        const string hint = "↑↓ Navigate  │  ←→ Change  │  ENTER Action  │  ESC Back";
        buffer.WriteAt((buffer.Width - hint.Length) / 2, boxBottom + 2, hint, ExoColors.ProksPale);
    }

    private void RenderPasswordChange(IRenderBuffer buffer, int left)
    {
        bool isConfirm = _mode == Mode.ConfirmPassword;
        int  top       = (buffer.Height - 7) / 2;
        string label   = isConfirm ? "Confirm: " : "New password: ";
        string masked  = new string('*', _input.Length);

        buffer.WriteAt(left, top,     "┌" + new string('─', BoxInner) + "┐", ExoColors.ProksBorder);
        buffer.WriteAt(left, top + 1, "│" + "  CHANGE PASSWORD".PadRight(BoxInner) + "│", ExoColors.ProksText);
        buffer.WriteAt(left, top + 2, "│" + new string('─', BoxInner) + "│", ExoColors.ProksBorder);
        buffer.WriteAt(left,                                    top + 3, "│", ExoColors.ProksBorder);
        buffer.WriteAt(left + 2,                                top + 3, label,  ExoColors.ProksPale);
        buffer.WriteAt(left + 2 + label.Length,                 top + 3, masked, ExoColors.PhosphorText);
        if (_blink.Visible)
            buffer.WriteAt(left + 2 + label.Length + masked.Length, top + 3, "_", ExoColors.PhosphorDim);
        buffer.WriteAt(left + BoxWidth - 1,                     top + 3, "│", ExoColors.ProksBorder);
        buffer.WriteAt(left, top + 4, "│" + new string(' ', BoxInner) + "│", ExoColors.ProksBorder);
        if (!string.IsNullOrEmpty(_message))
            buffer.WriteAt(left + 2, top + 4, _message, _messageIsError ? ExoColors.FaultText : ExoColors.ProksPale);
        buffer.WriteAt(left, top + 5, "│" + "  ENTER Confirm   ESC Cancel".PadRight(BoxInner) + "│", ExoColors.ProksPale);
        buffer.WriteAt(left, top + 6, "└" + new string('─', BoxInner) + "┘", ExoColors.ProksBorder);
    }

    private void RenderConfirmReset(IRenderBuffer buffer, int left)
    {
        int top = (buffer.Height - 7) / 2;
        buffer.WriteAt(left, top,     "┌" + new string('─', BoxInner) + "┐", ExoColors.FaultText);
        buffer.WriteAt(left, top + 1, "│" + "  RESET ALL DATA".PadRight(BoxInner) + "│", ExoColors.FaultText);
        buffer.WriteAt(left, top + 2, "│" + new string('─', BoxInner) + "│", ExoColors.ProksBorder);
        buffer.WriteAt(left, top + 3, "│" + "  This will permanently delete all operator accounts.".PadRight(BoxInner) + "│", ExoColors.ProksPale);
        buffer.WriteAt(left, top + 4, "│" + "  The application will close after reset.".PadRight(BoxInner) + "│", ExoColors.ProksPale);
        buffer.WriteAt(left, top + 5, "│" + "  Confirm? [Y/N]".PadRight(BoxInner) + "│", ExoColors.FaultText);
        buffer.WriteAt(left, top + 6, "└" + new string('─', BoxInner) + "┘", ExoColors.FaultText);
    }
}
