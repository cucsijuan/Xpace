using System;
using Godot;

namespace Xpace.Debug;

/// <summary>
/// The game menu's Debug page (scenes/menu/debug_menu.tscn): switches for what <see cref="DebugOptions"/> draws. Each
/// CheckBox under it named like an option of <see cref="DebugOptions"/> turns that option on and off; Back closes it.
/// </summary>
public partial class DebugMenu : Control
{
    /// <summary>Back to the game menu.</summary>
    public event Action Closed;

    public override void _Ready()
    {
        var lawZones = GetNode<CheckBox>("%LawZones");
        lawZones.ButtonPressed = DebugOptions.LawZones;
        lawZones.Toggled += on => DebugOptions.LawZones = on;
        GetNode<Button>("%Back").Pressed += Back;
        Hide();
    }

    public void Open()
    {
        GetNode<CheckBox>("%LawZones").ButtonPressed = DebugOptions.LawZones;
        Show();
        GetNode<CheckBox>("%LawZones").GrabFocus();
    }

    public void Back()
    {
        Hide();
        Closed?.Invoke();
    }
}
