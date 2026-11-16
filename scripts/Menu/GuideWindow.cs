using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Xpace.Cockpit;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Menu;

/// <summary>
/// The guide (scenes/menu/guide_window.tscn, in main): when a contract in the pilot's journal reaches a step with help
/// not read yet (the lead of an offer meant for them, the guide of work taken), it shows that help at the side of the
/// screen, over the game, which goes on. The guide key (<see cref="FlightInput.Guide"/>, F1) says it was read and
/// hides it; pressed again, it shows the help of the newest open contract again.
/// </summary>
public partial class GuideWindow : CanvasLayer
{
    [Export] public float LookSeconds = 0.5f;

    /// <summary>The help shown now (null: none).</summary>
    public JournalEntry Shown { get; private set; }

    private Label _title, _text, _key;
    private Control _panel;
    private double _since;
    private readonly HashSet<string> _read = new();     // read here, before the owner's answer comes back

    public override void _Ready()
    {
        _panel = GetNode<Control>("%Panel");
        _title = GetNode<Label>("%Title");
        _text = GetNode<Label>("%Text");
        _key = GetNode<Label>("%Key");
        _panel.Hide();
    }

    public override void _Process(double delta)
    {
        _since += delta;
        if (_since < LookSeconds || Shown != null)
            return;
        _since = 0;
        if (Pending() is JournalEntry entry)
            Show(entry);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed(FlightInput.Guide) || GameMenu.IsOpen)
            return;
        GetViewport().SetInputAsHandled();
        if (Shown != null)
            Read();
        else if (Universe.Current?.Me?.Journal.FirstOrDefault(j => j.Help != "" && j.State is ContractRecord.Offered or ContractRecord.Taken) is JournalEntry again)
            Show(again);
    }

    /// <summary>The first help in the journal nobody has read yet.</summary>
    public JournalEntry Pending()
    {
        PilotRecord me = Universe.Current?.Me;
        return me?.Journal.FirstOrDefault(j => j.Help != "" && j.State is ContractRecord.Offered or ContractRecord.Taken && !me.GuidesRead.Contains(j.HelpKey) && !_read.Contains(j.HelpKey));
    }

    private void Show(JournalEntry entry)
    {
        Shown = entry;
        _title.Text = entry.Title;
        _text.Text = entry.Help;
        _key.Text = $"{Keys()}  understood";
        _panel.Show();
    }

    /// <summary>The shown help is read: it hides, and the journal remembers.</summary>
    public void Read()
    {
        if (Shown == null)
            return;
        _read.Add(Shown.HelpKey);
        Universe.Current?.Ask(Contracts.Topic, JsonSerializer.Serialize(new ContractRequest { Do = ContractRequest.Read, Key = Shown.HelpKey }));
        Shown = null;
        _panel.Hide();
        _since = 0;
    }

    private static string Keys() =>
        InputMap.HasAction(FlightInput.Guide) && InputMap.ActionGetEvents(FlightInput.Guide).OfType<InputEventKey>().FirstOrDefault() is InputEventKey k
            ? OS.GetKeycodeString(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode) : "F1";
}
