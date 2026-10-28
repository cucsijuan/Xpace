using System;
using System.Text.Json;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.OnFoot;

/// <summary>
/// A station's lockers' screen, as a kiosk shows it (scenes/ui/locker_window.tscn): what the pilot carries on them
/// and what their locker here holds, side by side; Store and Take move a thing across. Requests go to the universe's
/// owner as the market's do (<see cref="Universe.Ask"/>). Esc or Close shuts it.
/// </summary>
public partial class LockerWindow : CanvasLayer, IKioskWindow
{
    public string Station { get; set; }

    public event Action Closed;

    /// <summary>The lockers as last shown (null until the first answer).</summary>
    public LockerView View { get; private set; }

    private Label _title, _carriedTitle, _message;
    private VBoxContainer _carried, _locker;

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _carriedTitle = GetNode<Label>("%CarriedTitle");
        _message = GetNode<Label>("%Message");
        _carried = GetNode<VBoxContainer>("%Carried");
        _locker = GetNode<VBoxContainer>("%Locker");
        GetNode<Button>("%Close").Pressed += Close;
        _title.Text = $"{Station} LOCKERS";
        _message.Text = "Connecting...";
        if (Universe.Current is Universe universe)
        {
            universe.Changed += Changed;
            TreeExiting += () => universe.Changed -= Changed;
        }
        Ask(LockerRequest.Look, 0);
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

    public void Ask(string what, int index) =>
        Universe.Current?.Ask(StationLockers.Topic, JsonSerializer.Serialize(new LockerRequest { Station = Station, Do = what, Index = index }));

    private void Changed(string topic)
    {
        if (topic != StationLockers.Topic || Universe.Current?.Document(topic) is not string text)
            return;
        LockerView view;
        try
        {
            view = JsonSerializer.Deserialize<LockerView>(text);
        }
        catch (JsonException)
        {
            return;
        }
        if (view?.Station == Station)
            Show(view);
    }

    private void Show(LockerView view)
    {
        View = view;
        _carriedTitle.Text = $"Carried  {view.Carried.Count} / {view.Slots}";
        _message.Text = view.Message;
        Fill(_carried, view.Carried, "Store", LockerRequest.Store, view.Present);
        Fill(_locker, view.Locker, "Take", LockerRequest.Take, view.Present && view.Carried.Count < view.Slots);
    }

    private void Fill(VBoxContainer list, System.Collections.Generic.List<ItemRecord> items, string label, string what, bool enabled)
    {
        foreach (Node n in list.GetChildren())
            n.QueueFree();
        if (items.Count == 0)
            list.AddChild(new Label { Text = "Empty", Modulate = new Color(1, 1, 1, 0.5f) });
        for (int i = 0; i < items.Count; i++)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = $"{items[i].Name}  ({items[i].Kind})", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var button = new Button { Text = label, Disabled = !enabled, FocusMode = Control.FocusModeEnum.None };
            int index = i;
            button.Pressed += () => Ask(what, index);
            row.AddChild(button);
            list.AddChild(row);
        }
    }
}
