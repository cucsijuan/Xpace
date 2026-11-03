using System;
using System.Text.Json;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.OnFoot;

/// <summary>
/// A station's arms dealer, as a kiosk shows it (scenes/ui/arms_window.tscn). Not a list of tonnes to trade like the
/// market's: a card for each model, with what it can do, how many the ship has aboard, and what the dealer has left
/// and whether its factory has anything to make more with. Buy loads one (rounds, a box) straight onto the ship, Fill as many as the
/// rack takes. Every request goes to the universe's owner (<see cref="Universe.Ask"/>), whose answer redraws the
/// screen; on a guest it comes a moment later. Esc or Close shuts it.
/// </summary>
public partial class ArmsWindow : CanvasLayer, IKioskWindow
{
    private static readonly Color Amber = new(1, 0.68f, 0.25f), Warning = new(1, 0.35f, 0.25f), Dim = new(1, 1, 1, 0.6f);

    /// <summary>The station whose dealer it shows, by callsign.</summary>
    public string Station { get; set; }

    public event Action Closed;

    /// <summary>What it showed last (null until the first answer).</summary>
    public ArmsView View { get; private set; }

    private Label _title, _credits, _message;
    private VBoxContainer _cards;

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _credits = GetNode<Label>("%Credits");
        _message = GetNode<Label>("%Message");
        _cards = GetNode<VBoxContainer>("%Cards");
        GetNode<Button>("%Close").Pressed += Close;
        _title.Text = $"{Station} ARMS";
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

    /// <summary>Asks the dealer for something (<see cref="MarketRequest"/>).</summary>
    public void Ask(string what, string model, int count) =>
        Universe.Current?.Ask(StationMarket.ArmsTopic, JsonSerializer.Serialize(new MarketRequest { Station = Station, Do = what, Good = model, Tonnes = count }));

    private void Changed(string topic)
    {
        if (topic != StationMarket.ArmsTopic || Universe.Current?.Document(topic) is not string text)
            return;
        ArmsView view;
        try
        {
            view = JsonSerializer.Deserialize<ArmsView>(text);
        }
        catch (JsonException)
        {
            return;
        }
        if (view?.Station == Station)
            Show(view);
    }

    private void Show(ArmsView view)
    {
        View = view;
        _credits.Text = $"Credits  {view.Credits:N0}";
        _message.Text = view.Message;
        foreach (Node n in _cards.GetChildren())
            n.QueueFree();
        foreach (ArmsOffer offer in view.Offers)
            _cards.AddChild(Card(offer, view));
    }

    private Control Card(ArmsOffer offer, ArmsView view)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.07f, 0.03f, 0.9f), BorderColor = Amber * new Color(1, 1, 1, 0.6f),
            BorderWidthLeft = 4, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        card.AddChild(row);

        var about = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var name = new Label { Text = offer.Name, ThemeTypeVariation = "HeaderMedium" };
        name.AddThemeColorOverride("font_color", Amber);
        about.AddChild(name);
        about.AddChild(new Label { Text = offer.About });
        about.AddChild(new Label { Text = offer.Details, Modulate = Dim });
        row.AddChild(about);

        var aboard = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) };   // room for a dispenser's ten
        aboard.AddChild(new Label { Text = "Aboard", Modulate = Dim });
        aboard.AddChild(new Label { Text = $"{Squares(offer.Aboard, offer.Rack)}  {offer.Aboard}/{offer.Rack}" });
        row.AddChild(aboard);

        var stock = new VBoxContainer { CustomMinimumSize = new Vector2(170, 0) };
        stock.AddChild(new Label { Text = "In stock", Modulate = Dim });
        stock.AddChild(new Label { Text = offer.Stock.ToString() });
        var factory = new Label { Text = offer.Starved ? $"Factory stopped: no {offer.MadeOf.ToLowerInvariant()}" : $"{offer.MadeOf} here: {offer.MadeOfStock} t" };
        factory.Modulate = offer.Starved ? Warning : Dim;
        stock.AddChild(factory);
        row.AddChild(stock);

        var buy = new VBoxContainer { CustomMinimumSize = new Vector2(110, 0) };
        buy.AddChild(new Label { Text = offer.Price < 0 ? "Sold out" : $"{offer.Price:N0} cr", HorizontalAlignment = HorizontalAlignment.Right });
        int room = offer.Rack - offer.Aboard;
        bool can = view.Docked && offer.Price >= 0 && room > 0 && view.Credits >= offer.Price;
        buy.AddChild(Button(offer.Lot > 1 ? $"Buy {offer.Lot}" : "Buy", offer.Id, Math.Min(offer.Lot, room), can));
        buy.AddChild(Button("Fill", offer.Id, room, can));
        row.AddChild(buy);
        return card;
    }

    // One square a piece, up to ten; a bigger rack, ten squares in proportion (a magazine of rounds)
    private static string Squares(int aboard, int rack)
    {
        int full = rack <= MaxSquares ? Math.Min(aboard, rack) : (int)Math.Ceiling(aboard * (double)MaxSquares / rack);
        int all = Math.Min(rack, MaxSquares);
        return new string('■', Math.Min(full, all)) + new string('□', Math.Max(0, all - full));
    }

    private const int MaxSquares = 10;

    private Button Button(string label, string model, int count, bool enabled)
    {
        var button = new Button { Text = label, Disabled = !enabled, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += () => Ask(MarketRequest.Buy, model, count);
        return button;
    }
}
