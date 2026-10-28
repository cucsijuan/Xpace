using System;
using System.Text.Json;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.OnFoot;

/// <summary>
/// A station market's screen, as a kiosk shows it (scenes/ui/market_window.tscn): the goods with their stock and
/// prices and what the hold has of each, the pilot's credits, hold and what they owe here, and what came of the last
/// request. Pay pays all that is owed; a wanted pilot who can't, surrenders (cargo, then the ship). Where a factor
/// works, what is owed to other stations shows below, each with what the factor asks to settle it.
/// Buy and Sell trade a tonne (with Shift, <see cref="ShiftTonnes"/>). Every request goes to the universe's owner
/// (<see cref="Universe.Ask"/>), whose answer redraws the screen; on a guest it comes a moment later.
/// Esc or Close shuts it.
/// </summary>
public partial class MarketWindow : CanvasLayer, IKioskWindow
{
    [Export] public int ShiftTonnes = 10;

    /// <summary>The station whose market it shows, by callsign.</summary>
    public string Station { get; set; }

    public event Action Closed;

    /// <summary>The market as it was last shown (null until the first answer).</summary>
    public MarketView View { get; private set; }

    private Label _title, _credits, _hold, _fines, _message;
    private GridContainer _rows, _factor;
    private Button _pay, _surrender, _close;

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _credits = GetNode<Label>("%Credits");
        _hold = GetNode<Label>("%Hold");
        _fines = GetNode<Label>("%Fines");
        _message = GetNode<Label>("%Message");
        _rows = GetNode<GridContainer>("%Rows");
        _factor = GetNode<GridContainer>("%Factor");
        _pay = GetNode<Button>("%Pay");
        _surrender = GetNode<Button>("%Surrender");
        _close = GetNode<Button>("%Close");
        _pay.Pressed += () => Ask(MarketRequest.Pay, null, 0);
        _surrender.Pressed += () => Ask(MarketRequest.Surrender, null, 0);
        _close.Pressed += Close;
        _title.Text = $"{Station} MARKET";
        _message.Text = "Connecting...";
        if (Universe.Current is Universe universe)
        {
            universe.Changed += Changed;
            TreeExiting += () => universe.Changed -= Changed;
        }
        Ask(MarketRequest.Look, null, 0);
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    public void Close()
    {
        Closed?.Invoke();
        QueueFree();
    }

    /// <summary>Asks the market to do something (<see cref="MarketRequest"/>).</summary>
    public void Ask(string what, string good, int tonnes) =>
        Universe.Current?.Ask(StationMarket.Topic, JsonSerializer.Serialize(new MarketRequest { Station = Station, Do = what, Good = good, Tonnes = tonnes }));

    private void Changed(string topic)
    {
        if (topic != StationMarket.Topic || Universe.Current?.Document(topic) is not string text)
            return;
        MarketView view;
        try
        {
            view = JsonSerializer.Deserialize<MarketView>(text);
        }
        catch (JsonException)
        {
            return;
        }
        if (view?.Station == Station)
            Show(view);
    }

    private void Show(MarketView view)
    {
        View = view;
        _credits.Text = $"Credits  {view.Credits:N0}";
        _hold.Text = $"Hold  {view.Carried} / {view.Capacity} t";
        _fines.Text = view.Owed == 0 ? view.Impounded ? "Ship impounded here" : "Nothing owed"
            : view.Bounty > 0 ? $"Owed  {view.Owed:N0}  (bounty {view.Bounty:N0})" : $"Fines owed  {view.Owed:N0}";
        _pay.Disabled = !view.Present || view.Owed == 0 && !view.Impounded;
        _surrender.Visible = view.Bounty > 0 && !view.Impounded && view.Credits < view.Owed;
        _surrender.Disabled = !view.Present;
        _message.Text = view.Message;
        foreach (Node n in _rows.GetChildren())
            n.QueueFree();
        foreach (string head in new[] { "Good", "Stock", "Buy", "Sell", "Hold", "", "" })
            _rows.AddChild(new Label { Text = head, ThemeTypeVariation = "HeaderSmall" });
        foreach (MarketLine line in view.Lines)
        {
            _rows.AddChild(new Label { Text = line.Name });
            _rows.AddChild(Number(line.Stock.ToString("N0")));
            _rows.AddChild(Number(line.Buy < 0 ? "-" : line.Buy.ToString("N0")));
            _rows.AddChild(Number(line.Sell < 0 ? "-" : line.Sell.ToString("N0")));
            _rows.AddChild(Number(line.Held > 0 ? $"{line.Held} t" : ""));
            _rows.AddChild(Trade("Buy", MarketRequest.Buy, line.Good, view.Docked && line.Buy >= 0));
            _rows.AddChild(Trade("Sell", MarketRequest.Sell, line.Good, view.Docked && line.Sell >= 0 && line.Held > 0));
        }
        ShowFactor(view);
    }

    private void ShowFactor(MarketView view)
    {
        foreach (Node n in _factor.GetChildren())
            n.QueueFree();
        _factor.Visible = view.Elsewhere.Count > 0;
        if (!_factor.Visible)
            return;
        foreach (string head in new[] { "Owed elsewhere", "", "Factor asks", "" })
            _factor.AddChild(new Label { Text = head, ThemeTypeVariation = "HeaderSmall" });
        foreach (DebtLine debt in view.Elsewhere)
        {
            _factor.AddChild(new Label { Text = debt.Station });
            _factor.AddChild(Number(debt.Owed.ToString("N0")));
            _factor.AddChild(Number(debt.Cost.ToString("N0")));
            var settle = new Button { Text = "Settle", Disabled = !view.Present || view.Credits < debt.Cost, FocusMode = Control.FocusModeEnum.None };
            string station = debt.Station;
            settle.Pressed += () => Ask(MarketRequest.Factor, station, 0);
            _factor.AddChild(settle);
        }
    }

    private static Label Number(string text) => new() { Text = text, HorizontalAlignment = HorizontalAlignment.Right };

    private Button Trade(string label, string what, string good, bool enabled)
    {
        var button = new Button { Text = label, Disabled = !enabled, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += () => Ask(what, good, Input.IsKeyPressed(Key.Shift) ? ShiftTonnes : 1);
        return button;
    }
}
