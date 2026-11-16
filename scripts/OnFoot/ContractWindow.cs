using System;
using System.Text.Json;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.OnFoot;

/// <summary>
/// A station's contract board, as its terminal shows it (scenes/ui/contract_window.tscn): the offers there (story work
/// first, marked), each with what it pays and a Take button, and the pilot's own contracts with what is left to do and
/// Collect, Hand over and Give up. Every request goes to the universe's owner (<see cref="Universe.Ask"/>), whose answer
/// redraws it. Esc or Close shuts it.
/// </summary>
public partial class ContractWindow : CanvasLayer, IKioskWindow
{
    public string Station { get; set; }

    public event Action Closed;

    /// <summary>The board as it was last shown (null until the first answer).</summary>
    public ContractView View { get; private set; }

    private Label _title, _credits, _message;
    private VBoxContainer _offers, _mine;
    private Label _offersEmpty, _mineEmpty;

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _credits = GetNode<Label>("%Credits");
        _message = GetNode<Label>("%Message");
        _offers = GetNode<VBoxContainer>("%Offers");
        _mine = GetNode<VBoxContainer>("%Mine");
        _offersEmpty = GetNode<Label>("%OffersEmpty");
        _mineEmpty = GetNode<Label>("%MineEmpty");
        GetNode<Button>("%Close").Pressed += Close;
        _title.Text = "CONTRACTS";
        _message.Text = "Connecting...";
        if (Universe.Current is Universe universe)
        {
            universe.Changed += Changed;
            TreeExiting += () => universe.Changed -= Changed;
        }
        Ask(ContractRequest.Look, "");
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

    public void Ask(string what, string id, string key = "") =>
        Universe.Current?.Ask(Contracts.Topic, JsonSerializer.Serialize(new ContractRequest { Station = Station, Do = what, Id = id, Key = key ?? "" }));

    private void Changed(string topic)
    {
        if (topic != Contracts.Topic || Universe.Current?.Document(topic) is not string text)
            return;
        ContractView view;
        try
        {
            view = JsonSerializer.Deserialize<ContractView>(text);
        }
        catch (JsonException)
        {
            return;
        }
        if (view?.Station == Station)
            Show(view);
    }

    private void Show(ContractView view)
    {
        View = view;
        _title.Text = $"{view.Title} CONTRACTS";
        _credits.Text = $"Credits  {view.Credits:N0}";
        _message.Text = view.Message;
        foreach (Node n in _offers.GetChildren())
            if (n != _offersEmpty)
                n.QueueFree();
        foreach (Node n in _mine.GetChildren())
            if (n != _mineEmpty)
                n.QueueFree();
        _offersEmpty.Visible = view.Offers.Count == 0;
        _mineEmpty.Visible = view.Mine.Count == 0;
        foreach (OfferLine offer in view.Offers)
        {
            string id = offer.Id;
            _offers.AddChild(Line(
                (offer.Story ? "▶ " : "") + offer.Title, $"{offer.Giver}  -  {offer.Reward:N0} cr" + (offer.Hours > 0 ? $"  -  {offer.Hours:0.#} h to do it" : ""), offer.Briefing,
                Button("Take", view.Present, () => Ask(ContractRequest.Take, id))));
        }
        foreach (MineLine mine in view.Mine)
        {
            string id = mine.Id;
            string left = mine.Left >= 0 ? $"  -  {Hours(mine.Left)} left" : "";
            Control line = Line((mine.Story ? "▶ " : "") + mine.Title, $"{mine.Reward:N0} cr{left}", mine.Objective,
                mine.CanCollect ? Button("Collect", true, () => Ask(ContractRequest.Collect, id)) : null,
                mine.CanHand ? Button("Hand over", true, () => Ask(ContractRequest.Hand, id)) : null,
                Button("Give up", true, () => Ask(ContractRequest.Abandon, id)));
            if (mine.CanReport)
            {
                Control report = ReportBox(id);
                line.AddChild(report);
                line.MoveChild(report, line.GetChildCount() - 2);     // above the separator
            }
            _mine.AddChild(line);
        }
    }

    // What the pilot reports: typed, then sent
    private Control ReportBox(string id)
    {
        var box = new HBoxContainer();
        var answer = new LineEdit { PlaceholderText = "What did you find?", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Name = "Answer" };
        answer.TextSubmitted += text => Ask(ContractRequest.Report, id, text);
        box.AddChild(answer);
        box.AddChild(Button("Report", true, () => Ask(ContractRequest.Report, id, answer.Text)));
        return box;
    }

    private static string Hours(double seconds) => seconds >= 3600 ? $"{seconds / 3600:0.0} h" : $"{Math.Max(0, seconds) / 60:0} min";

    private static Control Line(string title, string terms, string about, params Button[] buttons)
    {
        var box = new VBoxContainer();
        var top = new HBoxContainer();
        top.AddChild(new Label { Text = title, ThemeTypeVariation = "HeaderSmall", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        foreach (Button b in buttons)
            if (b != null)
                top.AddChild(b);
        box.AddChild(top);
        box.AddChild(new Label { Text = terms });
        if (!string.IsNullOrEmpty(about))
            box.AddChild(new Label { Text = about, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(560, 0), Modulate = new Color(1, 1, 1, 0.75f) });
        box.AddChild(new HSeparator());
        return box;
    }

    private static Button Button(string text, bool enabled, Action pressed)
    {
        var button = new Button { Text = text, Disabled = !enabled, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += pressed;
        return button;
    }
}
