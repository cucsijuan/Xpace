using System;
using Godot;
using Xpace.World;

namespace Xpace.Menu;

/// <summary>
/// The game menu's Journal page (scenes/menu/journal_page.tscn): the pilot's contracts (<see cref="PilotRecord.Journal"/>),
/// open ones first, each with its giver, reward, what is left to do, the briefing and the guide for where it stands.
/// Back closes it.
/// </summary>
public partial class JournalPage : Control
{
    public event Action Closed;

    private VBoxContainer _list;

    public override void _Ready()
    {
        _list = GetNode<VBoxContainer>("%List");
        GetNode<Button>("%Back").Pressed += Back;
        Hide();
    }

    public void Open()
    {
        Fill();
        Show();
        GetNode<Button>("%Back").GrabFocus();
    }

    public void Back()
    {
        Hide();
        Closed?.Invoke();
    }

    private void Fill()
    {
        foreach (Node n in _list.GetChildren())
            n.QueueFree();
        var journal = Universe.Current?.Me?.Journal;
        if (journal == null || journal.Count == 0)
        {
            _list.AddChild(new Label { Text = "No contracts yet. Look at the contracts terminal in a station's hangar." });
            return;
        }
        foreach (JournalEntry entry in journal)
        {
            bool open = entry.State is ContractRecord.Offered or ContractRecord.Taken;
            var box = new VBoxContainer { Modulate = open ? Colors.White : new Color(1, 1, 1, 0.55f) };
            box.AddChild(new Label { Text = (entry.Story ? "▶ " : "") + entry.Title, ThemeTypeVariation = "HeaderSmall" });
            string left = entry.Left >= 0 ? $"  -  {entry.Left / 3600:0.0} h left" : "";
            box.AddChild(new Label { Text = $"{entry.Giver}  -  {entry.Reward:N0} cr{left}  -  {entry.Objective}" });
            if (open && entry.Briefing != "")
                box.AddChild(Text(entry.Briefing, 0.8f));
            if (open && entry.Help != "")
                box.AddChild(Text(entry.Help, 1f, new Color(1, 0.8f, 0.45f)));
            box.AddChild(new HSeparator());
            _list.AddChild(box);
        }
    }

    private static Label Text(string text, float alpha, Color? colour = null) => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(600, 0),
        Modulate = (colour ?? Colors.White) with { A = alpha },
    };
}
