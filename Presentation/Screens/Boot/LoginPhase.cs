using ExoProxy.Core;
using ExoProxy.Data;

namespace ExoProxy.Presentation.Screens.Boot;

public sealed class LoginPhase : IBootPhase
{
    private const int BoxWidth = 62;
    private const int MaxSlots = 9;

    private readonly OperatorRegistry _registry;

    private enum InputMode { Login, Password, NewPassword, ConfirmPassword, Confirm }
    private InputMode _mode = InputMode.Login;

    private string _input = "";
    private string _loginBuffer = "";
    private string _passwordBuffer = "";
    private string _message = "";
    private bool _messageIsError;
    private BlinkState _blink;
    private int _selectedIndex = -1;

    public bool IsDone { get; private set; }
    public OperatorAccount? LoggedInAccount { get; private set; }

    // True when the operator logged in as DEV — the boot ceremony is skipped
    // and the base screen unlocks its testing commands.
    public bool IsDevLogin { get; private set; }

    public LoginPhase(OperatorRegistry registry)
    {
        _registry = registry;
    }

    public void Update(GameTime time, InputEvent? input)
    {
        var now = time.Total;
        _blink.Update(now);

        if (input is null) return;

        var key = input.Value.Key;

        if (_mode == InputMode.Login)
        {
            if (key.Key == ConsoleKey.UpArrow)
            {
                var slots = GetVisualSlots();
                int next = _selectedIndex <= 0 ? 0 : _selectedIndex - 1;
                _selectedIndex = next;
                _input = slots[_selectedIndex]?.Login ?? "";
                _message = "";
                return;
            }
            if (key.Key == ConsoleKey.DownArrow)
            {
                var slots = GetVisualSlots();
                int next = _selectedIndex < 0 ? 0 : Math.Min(MaxSlots - 1, _selectedIndex + 1);
                _selectedIndex = next;
                _input = slots[_selectedIndex]?.Login ?? "";
                _message = "";
                return;
            }
            if (key.Key == ConsoleKey.Delete)
            {
                var slots = GetVisualSlots();
                if (_selectedIndex >= 0 && _selectedIndex < MaxSlots)
                {
                    var acc = slots[_selectedIndex];
                    if (acc is null)
                    {
                        _message        = "No operator in this slot.";
                        _messageIsError = true;
                    }
                    else if (acc.Status == OperatorStatus.Active)
                    {
                        acc.Status      = OperatorStatus.Redacted;
                        _registry.Save();
                        _message        = $"Operator {acc.Login} redacted from local registry.";
                        _messageIsError = false;
                        _input          = "";
                        _selectedIndex  = -1;
                    }
                    else
                    {
                        _message        = $"Cannot redact {acc.Status.ToString().ToUpper()} operator.";
                        _messageIsError = true;
                    }
                }
                return;
            }
        }

        if (_mode == InputMode.Confirm)
        {
            if (key.KeyChar == 'y' || key.KeyChar == 'Y')
            {
                _registry.Register(_loginBuffer, _passwordBuffer);
                _message        = $"Operator {_loginBuffer.ToUpper()} registered.";
                _messageIsError = false;
                _input          = "";
                _loginBuffer    = "";
                _passwordBuffer = "";
                _mode           = InputMode.Login;
            }
            else if (key.KeyChar == 'n' || key.KeyChar == 'N')
            {
                _message        = "";
                _input          = "";
                _loginBuffer    = "";
                _passwordBuffer = "";
                _mode           = InputMode.Login;
            }
            // ESC falls through to the handler below
            else if (key.Key != ConsoleKey.Escape)
            {
                return;
            }
        }

        if (key.Key == ConsoleKey.Escape)
        {
            if (_mode is InputMode.Password or InputMode.NewPassword or InputMode.ConfirmPassword)
            {
                _input          = _loginBuffer;
                _loginBuffer    = "";
                _passwordBuffer = "";
                _mode           = InputMode.Login;
                _message        = "";
                var slots = GetVisualSlots();
                _selectedIndex  = slots.FindIndex(a => a?.Login == _input.ToUpper());
            }
            else
            {
                _input          = "";
                _loginBuffer    = "";
                _passwordBuffer = "";
                _message        = "";
                _mode           = InputMode.Login;
                _selectedIndex  = -1;
            }
            return;
        }

        if (_mode == InputMode.Confirm) return;

        if (key.Key == ConsoleKey.Backspace)
        {
            if (_input.Length > 0)
            {
                _input = _input[..^1];
                if (_mode == InputMode.Login) SyncSelectorToInput();
                if (_messageIsError) _message = "";
            }
            return;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            HandleEnter();
            return;
        }

        int maxInputLen = _mode == InputMode.Login ? 12 : 24;
        if (key.KeyChar != '\0' && _input.Length < maxInputLen)
        {
            _input += _mode == InputMode.Login ? char.ToUpper(key.KeyChar) : key.KeyChar;
            if (_mode == InputMode.Login) SyncSelectorToInput();
            if (_messageIsError) _message = "";
        }
    }

    private List<OperatorAccount?> GetVisualSlots()
    {
        var result = new List<OperatorAccount?>(MaxSlots);

        foreach (var acc in _registry.Accounts.Where(a => a.Status == OperatorStatus.Active))
            result.Add(acc);

        int emptyCount = MaxSlots - _registry.Accounts.Count;
        for (int i = 0; i < emptyCount; i++)
            result.Add(null);

        foreach (var acc in _registry.Accounts.Where(a => a.Status != OperatorStatus.Active))
            result.Add(acc);

        return result;
    }

    private string GetLoginHint()
    {
        if (_input.Length == 0)
            return "Type a login to continue,  or  ↑↓  to browse registry";

        if (_registry.LoginExists(_input))
        {
            var acc = _registry.Accounts.First(a => a.Login == _input.ToUpper());
            return acc.Status switch
            {
                OperatorStatus.Redacted   => "Operator data redacted — access unavailable",
                OperatorStatus.Terminated => "Operator terminated — access revoked",
                _                         => "Existing operator — ENTER to continue",
            };
        }

        return _registry.Accounts.Count < MaxSlots
            ? "Unknown operator — ENTER to register"
            : "Registry full — maximum 9 operators";
    }

    private void SyncSelectorToInput()
    {
        var slots = GetVisualSlots();
        _selectedIndex = slots.FindIndex(a => a?.Login == _input.ToUpper());
    }

    private void HandleEnter()
    {
        switch (_mode)
        {
            case InputMode.Login:
                if (_input.Length == 0) return;

                // DEV is a reserved login for the team — no password, no
                // registry entry, straight to the base screen. Its save files
                // (*_DEV.yaml) are separate from real operator data.
                if (_input.ToUpper() == "DEV")
                {
                    LoggedInAccount = new OperatorAccount
                    {
                        Login          = "DEV",
                        PasswordHash   = "",
                        RegisteredDate = "—",
                        Status         = OperatorStatus.Active
                    };
                    IsDevLogin = true;
                    IsDone     = true;
                    return;
                }

                if (_registry.LoginExists(_input))
                {
                    var acc = _registry.Accounts.First(a => a.Login == _input.ToUpper());
                    if (acc.Status == OperatorStatus.Redacted)
                    {
                        _message        = "Operator data redacted. Access unavailable.";
                        _messageIsError = true;
                        return;
                    }
                    if (acc.Status == OperatorStatus.Terminated)
                    {
                        _message        = "Operator terminated. Access revoked.";
                        _messageIsError = true;
                        return;
                    }
                    _loginBuffer = _input.ToUpper();
                    _input       = "";
                    _mode        = InputMode.Password;
                    _message     = "";
                }
                else if (_registry.Accounts.Count < MaxSlots)
                {
                    _loginBuffer    = _input;
                    _input          = "";
                    _mode           = InputMode.NewPassword;
                    _message        = $"New operator: {_loginBuffer.ToUpper()}. Set password.";
                    _messageIsError = false;
                }
                else
                {
                    _message        = "Registry full. Maximum 9 operators.";
                    _messageIsError = true;
                    _input          = "";
                }
                break;

            case InputMode.Password:
                if (_registry.TryLogin(_loginBuffer, _input, out var account))
                {
                    LoggedInAccount = account;
                    IsDone          = true;
                }
                else
                {
                    _message        = "Access denied. [ESC] to change login.";
                    _messageIsError = true;
                    _input          = "";
                }
                break;

            case InputMode.NewPassword:
                if (_input.Length == 0) return;
                _passwordBuffer = _input;
                _input          = "";
                _mode           = InputMode.ConfirmPassword;
                _message        = "Confirm password.";
                _messageIsError = false;
                break;

            case InputMode.ConfirmPassword:
                if (_input.Length == 0) return;
                if (_input == _passwordBuffer)
                {
                    _input          = "";
                    _message        = $"Register operator {_loginBuffer.ToUpper()}? [Y/N]";
                    _messageIsError = false;
                    _mode           = InputMode.Confirm;
                }
                else
                {
                    _message        = "Passwords do not match. Try again.";
                    _messageIsError = true;
                    _input          = "";
                    _passwordBuffer = "";
                    _mode           = InputMode.NewPassword;
                }
                break;

            case InputMode.Confirm:
                break;
        }
    }

    public void Render(IRenderBuffer buffer)
    {
        int boxX = (buffer.Width - BoxWidth) / 2;
        int boxY = (buffer.Height - (MaxSlots + 7)) / 2;

        string title = "REMOTE ACQUISITION DIVISION — Operator Registry";
        buffer.WriteAt((buffer.Width - title.Length) / 2, boxY - 2, title, ExoColors.ProksText);
        buffer.WriteAt(boxX, boxY - 1, new string('─', BoxWidth), ExoColors.ProksBorder);

        buffer.WriteAt(boxX, boxY, "┌" + new string('─', BoxWidth - 2) + "┐", ExoColors.ProksBorder);

        var visualSlots = GetVisualSlots();
        for (int i = 0; i < MaxSlots; i++)
        {
            int row = boxY + 1 + i;

            buffer.WriteAt(boxX, row, "│", ExoColors.ProksBorder);
            buffer.WriteAt(boxX + BoxWidth - 1, row, "│", ExoColors.ProksBorder);

            var acc = visualSlots[i];
            if (acc is not null)
            {
                bool selected = _mode == InputMode.Login && i == _selectedIndex;
                string color = selected ? ExoColors.PhosphorText : ExoColors.ProksPale;

                bool redacted = acc.Status == OperatorStatus.Redacted;
                string selector = selected ? "►" : " ";
                string login = redacted ? new string('█', acc.Login.Length).PadRight(14) : acc.Login.PadRight(14);
                string content = $"{selector} [{i + 1}] {login} {acc.RegisteredDate.PadRight(18)} ";
                buffer.WriteAt(boxX + 2, row, content, color);

                string statusText = redacted ? "[REDACTED]" : acc.Status == OperatorStatus.Terminated ? "TERMINATED" : "ACTIVE";
                string statusColor = redacted ? ExoColors.ProksDark : acc.Status == OperatorStatus.Terminated ? ExoColors.FaultText : ExoColors.ProksText;
                buffer.WriteAt(boxX + 2 + content.Length, row, statusText, statusColor);
            }
            else
            {
                bool selected = _mode == InputMode.Login && i == _selectedIndex;
                string selector = selected ? "►" : " ";
                string color = selected ? ExoColors.PhosphorText : ExoColors.ProksDark;
                buffer.WriteAt(boxX + 2, row, $"{selector} [{i + 1}] —", color);
            }
        }

        buffer.WriteAt(boxX, boxY + 1 + MaxSlots, "└" + new string('─', BoxWidth - 2) + "┘", ExoColors.ProksBorder);

        int promptY = boxY + MaxSlots + 3;

        string lineAbove = !string.IsNullOrEmpty(_message)
            ? _message
            : _mode == InputMode.Login ? GetLoginHint() : "";

        bool lineAboveIsError = !string.IsNullOrEmpty(_message) && _messageIsError;

        if (!string.IsNullOrEmpty(lineAbove))
        {
            int msgX = (buffer.Width - lineAbove.Length) / 2;
            buffer.WriteAt(msgX, promptY - 1, lineAbove, lineAboveIsError ? ExoColors.FaultText : ExoColors.ProksPale);
        }

        string prompt = _mode switch
        {
            InputMode.Login => "Login: ",
            InputMode.Password => $"Password [{_loginBuffer}]: ",
            InputMode.NewPassword => "New password: ",
            InputMode.ConfirmPassword => "Confirm password: ",
            InputMode.Confirm => "[Y/N]: ",
            _ => "Login: "
        };

        bool maskInput = _mode is InputMode.Password or InputMode.NewPassword or InputMode.ConfirmPassword;
        string displayed = maskInput ? new string('*', _input.Length) : _input;
        int promptX = (buffer.Width - prompt.Length - displayed.Length) / 2;

        buffer.WriteAt(promptX, promptY, prompt, ExoColors.ProksPale);
        buffer.WriteAt(promptX + prompt.Length, promptY, displayed, ExoColors.PhosphorText);

        if (_blink.Visible)
            buffer.WriteAt(promptX + prompt.Length + displayed.Length, promptY, "_", ExoColors.PhosphorDim);

        string helpBar = _mode == InputMode.Login
            ? "↑↓ Navigate   ENTER Select   DEL Redact   ESC Reset"
            : "ENTER Confirm   ESC Back";
        buffer.WriteAt((buffer.Width - helpBar.Length) / 2, promptY + 2, helpBar, ExoColors.ProksDark);
    }
}
