using System;
using Godot;
using Xpace.Space;

namespace Xpace.OnFoot;

/// <summary>A kiosk's window: the station it serves is set before it opens; it says when it closes.</summary>
public interface IKioskWindow
{
    string Station { get; set; }

    event Action Closed;
}

/// <summary>
/// A kiosk of the station (scenes/stations/props: the market's, the lockers'), anywhere inside it: a walker at it
/// (within <see cref="UseDistance"/> of its front, <see cref="Front"/>) uses it to open its window
/// (<see cref="WindowScene"/>, an <see cref="IKioskWindow"/>), which frees the mouse and holds the walker still until
/// it is closed. It serves the station it is part of (<see cref="StationRadio.Above"/>).
/// </summary>
public partial class StationKiosk : Node3D, IWalkerUse
{
    [Export] public PackedScene WindowScene;
    /// <summary>What using it does, for the walker's prompt.</summary>
    [Export] public string Prompt = "trade";
    /// <summary>Where a walker stands to use it; none: the kiosk itself.</summary>
    [Export] public Node3D Front;
    [Export] public float UseDistance = 1.6f;

    /// <summary>The window open now, or null.</summary>
    public Node Window { get; private set; }

    public override void _Ready() => AddToGroup(Walker.UsesGroup);

    private string Station => StationRadio.Above(this)?.Sign;

    public string Offer(Walker walker) =>
        Window == null && WindowScene != null && Station != null &&
        walker.GlobalPosition.DistanceTo((Front ?? this).GlobalPosition) < UseDistance ? Prompt : null;

    public void Use(Walker walker)
    {
        if (Window != null || Station is not string station || WindowScene?.Instantiate() is not IKioskWindow window)
            return;
        Window = (Node)window;
        window.Station = station;
        walker.Busy = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        window.Closed += () =>
        {
            Window = null;
            if (IsInstanceValid(walker))
            {
                walker.Busy = false;
                walker.TakeControl();
            }
        };
        GetTree().Root.AddChild(Window);
    }
}
