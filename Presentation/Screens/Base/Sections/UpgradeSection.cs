using ExoProxy.Core;
using ExoProxy.Data;

namespace ExoProxy.Presentation.Screens.Base.Sections;

public sealed class RoverUpgradeSection : IBaseSection
{
    public string SectionId => SectionIds.Upgrade;

    public BaseSectionResponse Response { get; private set; }
        = new(BaseSectionRequest.Stay, null);

    private readonly RoverStats _rover;
    private readonly OperatorAccount _account;
    private readonly OperatorRegistry _registry;

    private readonly List<UpgradeItem> _items;

    private int _selected;
    private string _message = "";
    private bool _messageIsError;

    private BlinkState _blink;

    private const int BoxWidth = 70;
    private const int BoxInner = BoxWidth - 2;
    private const int BoxTop = 2;

    public RoverUpgradeSection(
        RoverStats rover,
        OperatorAccount account,
        OperatorRegistry registry)
    {
        _rover = rover;
        _account = account;
        _registry = registry;

        _items = BuildItems();
    }

private sealed class UpgradeItem
{
    public string Label { get; init; } = "";

    public Func<string> CurrentValue { get; init; } = null!;
    public Func<string> NextValue { get; init; } = null!;

    public Func<int> Cost { get; init; } = null!;

    public Action Upgrade { get; init; } = null!;
}
private List<UpgradeItem> BuildItems()
{
    return
    [
        new()
        {
            Label = "Battery Capacity",

            CurrentValue = () =>
                _rover.BatteryCapacity.ToString(),

            NextValue = () =>
                (_rover.BatteryCapacity + 200).ToString(),

            Cost = () =>
                100 + _rover.BatteryCapacity / 10,

            Upgrade = () =>
                _rover.BatteryCapacity += 200
        },

        new()
        {
            Label = "ESD Resistance",

            CurrentValue = () =>
                _rover.ESDResistance.ToString("0.00"),

            NextValue = () =>
                (_rover.ESDResistance + 0.05).ToString("0.00"),

            Cost = () => 250,

            Upgrade = () =>
                _rover.ESDResistance += 0.05
        },

        new()
        {
            Label = "EM Resistance",

            CurrentValue = () =>
                _rover.EMResistance.ToString("0.00"),

            NextValue = () =>
                (_rover.EMResistance + 0.05).ToString("0.00"),

            Cost = () => 250,

            Upgrade = () =>
                _rover.EMResistance += 0.05
        },

        new()
        {
            Label = "Chassis Tightness",

            CurrentValue = () =>
                _rover.ChassisTightness.ToString("0.00"),

            NextValue = () =>
                (_rover.ChassisTightness + 0.05).ToString("0.00"),

            Cost = () => 300,

            Upgrade = () =>
                _rover.ChassisTightness += 0.05
        },

        new()
        {
            Label = "High Temp Resistance",

            CurrentValue = () =>
                _rover.HighTemperatureResistance.ToString("0.00"),

            NextValue = () =>
                (_rover.HighTemperatureResistance + 0.05).ToString("0.00"),

            Cost = () => 350,

            Upgrade = () =>
                _rover.HighTemperatureResistance += 0.05
        },

        new()
        {
            Label = "Low Temp Resistance",

            CurrentValue = () =>
                _rover.LowTemperatureResistance.ToString("0.00"),

            NextValue = () =>
                (_rover.LowTemperatureResistance + 0.05).ToString("0.00"),

            Cost = () => 350,

            Upgrade = () =>
                _rover.LowTemperatureResistance += 0.05
        },

        new()
        {
            Label = "Radiation Resistance",

            CurrentValue = () =>
                _rover.RadiationResistance.ToString("0.00"),

            NextValue = () =>
                (_rover.RadiationResistance + 0.05).ToString("0.00"),

            Cost = () => 400,

            Upgrade = () =>
                _rover.RadiationResistance += 0.05
        },

        new()
        {
            Label = "Max Health",

            CurrentValue = () =>
                _rover.MaxHealth.ToString(),

            NextValue = () =>
                (_rover.MaxHealth + 20).ToString(),

            Cost = () =>
                150 + _rover.MaxHealth * 2,

            Upgrade = () =>
                _rover.MaxHealth += 20
        }
    ];
}
public void Update(GameTime time, InputEvent? input)
{
    var now = time.Total;
    _blink.Update(now);

    Response = new(BaseSectionRequest.Stay, null);

    if (input is null)
        return;

    var key = input.Value.Key;

    if (key.Key == ConsoleKey.Escape)
    {
        Response =
            new(BaseSectionRequest.GoToHub, null);
        return;
    }

    if (key.Key == ConsoleKey.UpArrow)
    {
        if (_selected > 0)
            _selected--;

        return;
    }

    if (key.Key == ConsoleKey.DownArrow)
    {
        if (_selected < _items.Count - 1)
            _selected++;

        return;
    }

    if (key.Key == ConsoleKey.Enter)
    {
        PurchaseUpgrade();
    }
}

private void PurchaseUpgrade()
{
    var item = _items[_selected];

    int cost = item.Cost();

    if (_account.Funds < cost)
    {
        _message = "Insufficient funds.";
        _messageIsError = true;
        return;
    }

    _account.Funds -= cost;

    item.Upgrade();

    _rover.Save();
    _registry.Save();

    _message = "Upgrade installed.";
    _messageIsError = false;
}
public void Render(IRenderBuffer buffer)
{
    int left = (buffer.Width - BoxWidth) / 2;

    buffer.WriteAt(
        left,
        BoxTop,
        "┌── ROVER UPGRADES " +
        new string('─', BoxInner - 19) + "┐",
        ExoColors.ProksBorder);

    for (int i = 0; i < _items.Count; i++)
    {
        var item = _items[i];

        bool selected = i == _selected;

        int row = BoxTop + 2 + i;

        buffer.WriteAt(
            left,
            row,
            "│",
            ExoColors.ProksBorder);

        buffer.WriteAt(
            left + BoxWidth - 1,
            row,
            "│",
            ExoColors.ProksBorder);

        if (selected)
        {
            buffer.WriteAt(
                left + 2,
                row,
                _blink.Visible ? "►" : "▷",
                ExoColors.PhosphorText);
        }

        string line =
            $"{item.Label,-28} " +
            $"{item.CurrentValue()} → {item.NextValue()}";

        buffer.WriteAt(
            left + 4,
            row,
            line,
            selected
                ? ExoColors.PhosphorText
                : ExoColors.ProksPale);

        string cost = $"${item.Cost()}";

        buffer.WriteAt(
            left + BoxWidth - cost.Length - 3,
            row,
            cost,
            ExoColors.ProksText);
    }

    int bottom = BoxTop + _items.Count + 3;

    buffer.WriteAt(
        left,
        bottom,
        "└" + new string('─', BoxInner) + "┘",
        ExoColors.ProksBorder);

    string wallet = $"$: {_account.Funds}";

    buffer.WriteAt(
        left + 3,
        bottom + 1,
        wallet,
        ExoColors.ProksText);

    if (!string.IsNullOrEmpty(_message))
    {
        buffer.WriteAt(
            left + 20,
            bottom + 1,
            _message,
            _messageIsError
                ? ExoColors.FaultText
                : ExoColors.ProksPale);
    }

    const string hint =
        "↑↓ Navigate  │  ENTER Upgrade  │  ESC Back";

    buffer.WriteAt(
        (buffer.Width - hint.Length) / 2,
        bottom + 2,
        hint,
        ExoColors.ProksPale);
}

}

