using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.OnFoot;

/// <summary>
/// A station's arms dealer, as a kiosk shows it (scenes/ui/arms_window.tscn). Not a list of tonnes to trade like the
/// market's: a card for each model, with what it can do, how many the ship has aboard, and what the dealer has left
/// and whether its factory has anything to make more with. Buy loads one (rounds, a box) straight onto the ship, Fill as many as the
/// rack takes. A laser or a turret bought goes to the pilot's locker here, and the Fitting tab fits it: the ship
/// turns on a stand, with what is on each hardpoint, and a piece fitted appears in a puff of dust (for now: a retrofit
/// hangar with the ship in it comes later). Every request goes to the universe's owner (<see cref="Universe.Ask"/>),
/// whose answer redraws the screen; on a guest it comes a moment later. Esc or Close shuts it.
/// </summary>
public partial class ArmsWindow : CanvasLayer, IKioskWindow
{
    private static readonly Color Amber = new(1, 0.68f, 0.25f), Warning = new(1, 0.35f, 0.25f), Dim = new(1, 1, 1, 0.6f);

    /// <summary>The station whose dealer it shows, by callsign.</summary>
    public string Station { get; set; }

    public event Action Closed;

    /// <summary>What a piece fitted appears in (scenes/fx/fit_puff.tscn).</summary>
    [Export] public PackedScene PuffScene;

    /// <summary>What it showed last (null until the first answer).</summary>
    public ArmsView View { get; private set; }

    /// <summary>The ship's fit as it showed it last (null until the first answer).</summary>
    public FittingView Fit { get; private set; }

    /// <summary>The ship on the Fitting tab's stand (null until the first answer).</summary>
    public Node3D Ship { get; private set; }

    /// <summary>How many puffs of dust it has shown: a piece appeared.</summary>
    public int Puffs { get; private set; }

    private Label _title, _credits, _message;
    private VBoxContainer _cards, _places, _locker;
    private TabContainer _tabs;
    private TurnTable _turntable;
    private Node3D _stage;
    private string _shownHull;
    private readonly Dictionary<string, string> _shown = new();

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _credits = GetNode<Label>("%Credits");
        _message = GetNode<Label>("%Message");
        _cards = GetNode<VBoxContainer>("%Dealer");
        _places = GetNode<VBoxContainer>("%Places");
        _locker = GetNode<VBoxContainer>("%Locker");
        _tabs = GetNode<TabContainer>("%Tabs");
        _turntable = GetNode<TurnTable>("%Turntable");
        _stage = GetNode<Node3D>("%Stage");
        GetNode<Control>("%ShipView").GuiInput += Dragged;
        GetNode<Button>("%Close").Pressed += Close;
        _title.Text = $"{Station} ARMS";
        _message.Text = "Connecting...";
        if (Universe.Current is Universe universe)
        {
            universe.Changed += Changed;
            TreeExiting += () => universe.Changed -= Changed;
        }
        Ask(MarketRequest.Look, null, 0);
        AskFitting(FittingRequest.Look, null, 0);
    }

    /// <summary>Shows the dealer (0) or the fitting (1).</summary>
    public void ShowTab(int tab) => _tabs.CurrentTab = tab;

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
    public void Ask(string what, string model, int count)
    {
        Universe.Current?.Ask(StationMarket.ArmsTopic, JsonSerializer.Serialize(new MarketRequest { Station = Station, Do = what, Good = model, Tonnes = count }));
        if (what != MarketRequest.Look)
            AskFitting(FittingRequest.Look, null, 0);       // a piece bought shows in the locker
    }

    /// <summary>Asks the fitters for something (<see cref="FittingRequest"/>).</summary>
    public void AskFitting(string what, string place, int index)
    {
        Universe.Current?.Ask(FittingDesk.Topic, JsonSerializer.Serialize(new FittingRequest { Station = Station, Do = what, Place = place, Index = index }));
        if (what != FittingRequest.Look)
            Ask(MarketRequest.Look, null, 0);               // the dealer's cards count what is fitted
    }

    private void Changed(string topic)
    {
        if (topic == FittingDesk.Topic)
        {
            FittingChanged();
            return;
        }
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
        if (offer.Piece)
        {
            aboard.AddChild(new Label { Text = "Fitted / in your locker", Modulate = Dim });
            aboard.AddChild(new Label { Text = $"{offer.Fitted} / {offer.Kept}" });
        }
        else
        {
            aboard.AddChild(new Label { Text = "Aboard", Modulate = Dim });
            aboard.AddChild(new Label { Text = $"{Squares(offer.Aboard, offer.Rack)}  {offer.Aboard}/{offer.Rack}" });
        }
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
        int room = offer.Piece ? 1 : offer.Rack - offer.Aboard;
        bool can = view.Docked && offer.Price >= 0 && room > 0 && view.Credits >= offer.Price;
        buy.AddChild(Button(offer.Lot > 1 ? $"Buy {offer.Lot}" : "Buy", offer.Id, Math.Min(offer.Lot, room), can));
        if (!offer.Piece)
            buy.AddChild(Button("Fill", offer.Id, room, can));
        else
        {
            var sell = new Button
            {
                Text = offer.SellPrice >= 0 ? $"Sell {offer.SellPrice:N0}" : "Sell", FocusMode = Control.FocusModeEnum.None,
                Disabled = !view.Docked || offer.Kept == 0 || offer.SellPrice < 0,
                TooltipText = "Sell one from your locker here",
            };
            sell.Pressed += () => Ask(MarketRequest.Sell, offer.Id, 1);
            buy.AddChild(sell);
        }
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

    private void FittingChanged()
    {
        if (Universe.Current?.Document(FittingDesk.Topic) is not string text)
            return;
        FittingView view;
        try
        {
            view = JsonSerializer.Deserialize<FittingView>(text);
        }
        catch (JsonException)
        {
            return;
        }
        if (view?.Station != Station)
            return;
        Fit = view;
        if (!string.IsNullOrEmpty(view.Message))
            _message.Text = view.Message;
        Stand(view);
        ListPlaces(view);
        ListLocker(view);
    }

    // The ship on the stand, with what is on each hardpoint; a piece that appears comes in a puff of dust
    private void Stand(FittingView view)
    {
        bool first = view.Hull != _shownHull;
        if (first)
        {
            Ship?.QueueFree();
            Ship = null;
            _shown.Clear();
            _shownHull = view.Hull;
            if (ResourceLoader.Exists(view.Hull) && GD.Load<ShipSpec>(view.Hull)?.Exterior?.Instantiate() is Node3D outside)
            {
                Ship = outside;
                _turntable.AddChild(outside);
            }
        }
        if (Ship == null)
            return;
        var hardpoints = Ship.FindChildren("*", "", true, false).OfType<Hardpoint>().ToDictionary(h => h.Name.ToString());
        foreach (FittingPlace place in view.Places)
        {
            if (!hardpoints.TryGetValue(place.Name, out Hardpoint hardpoint) || _shown.GetValueOrDefault(place.Name) == place.Mounted)
                continue;
            _shown[place.Name] = place.Mounted;
            Resource model = place.Mounted != "" && ResourceLoader.Exists(place.Mounted) ? GD.Load<Resource>(place.Mounted) : null;
            hardpoint.Mount(model, working: false);
            if (model != null && !first)
                Puff(hardpoint.GlobalPosition);
        }
    }

    private void Puff(Vector3 at)
    {
        if (PuffScene?.Instantiate() is not Node3D puff)
            return;
        puff.Position = _stage.ToLocal(at);         // before it enters the tree: it starts emitting there
        _stage.AddChild(puff);
        Puffs++;
    }

    private void ListPlaces(FittingView view)
    {
        foreach (Node n in _places.GetChildren())
            n.QueueFree();
        foreach (FittingPlace place in view.Places)
        {
            var row = new HBoxContainer();
            string kinds = place.Accepts switch
            {
                MountKinds.Fixed => "fixed",
                MountKinds.Turret => "turret",
                _ => "fixed or turret",
            };
            var about = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var name = new Label { Text = place.Model != "" ? place.Model : "Empty" };
            name.AddThemeColorOverride("font_color", place.Model != "" ? Amber : Dim);
            about.AddChild(name);
            about.AddChild(new Label { Text = $"{place.Name}  (size {place.Size}, {kinds})", Modulate = Dim });
            row.AddChild(about);
            var remove = new Button { Text = "Remove", Disabled = !view.Docked || place.Mounted == "", FocusMode = Control.FocusModeEnum.None };
            string target = place.Name;
            remove.Pressed += () => AskFitting(FittingRequest.Remove, target, 0);
            row.AddChild(remove);
            _places.AddChild(row);
        }
    }

    private void ListLocker(FittingView view)
    {
        foreach (Node n in _locker.GetChildren())
            n.QueueFree();
        if (view.Locker.Count == 0)
            _locker.AddChild(new Label { Text = "No weapons. Buy them at the dealer.", Modulate = Dim });
        foreach (FittingPiece piece in view.Locker)
        {
            var box = new VBoxContainer();
            var name = new Label { Text = piece.Name };
            name.AddThemeColorOverride("font_color", Amber);
            box.AddChild(name);
            var buttons = new HFlowContainer();
            if (piece.FitsOn.Count == 0)
                buttons.AddChild(new Label { Text = "Fits no hardpoint of this ship", Modulate = Dim });
            foreach (string place in piece.FitsOn)
            {
                var fit = new Button { Text = $"Fit on {place}", Disabled = !view.Docked, FocusMode = Control.FocusModeEnum.None };
                int index = piece.Index;
                string target = place;
                fit.Pressed += () => AskFitting(FittingRequest.Fit, target, index);
                buttons.AddChild(fit);
            }
            box.AddChild(buttons);
            _locker.AddChild(box);
        }
    }

    private void Dragged(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0)
            _turntable.Drag(motion.Relative.X);
    }
}
