using Godot;
using Xpace.Cockpit;
using Xpace.Menu;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.OnFoot;

/// <summary>
/// The pilot's seat in a ship: while the ship is docked on a landing pad (<see cref="LandingPad.PadState.Docked"/>),
/// Leave the seat (Y) stands the pilot up and out beside the ship as a <see cref="Walker"/>, in the hangar's frame,
/// with the pilot's own view and controls stopped (<see cref="Pilot.Stand"/>). Back at the ship's side, Use (E)
/// boards it again. The ship stays on the pad meanwhile, its computer running.
/// </summary>
public partial class ShipSeat : Node
{
    [Export] public Pilot Pilot;
    [Export] public PackedScene WalkerScene;
    /// <summary>Where the pilot steps out; none: on the pad in front of the nose, a little to its left.</summary>
    [Export] public Node3D Exit;
    /// <summary>Shown only while the pilot is in the seat (the cockpit's inside).</summary>
    [Export] public Node3D[] Inside = System.Array.Empty<Node3D>();
    /// <summary>Shown only while the pilot is out (the hull's outside, which would hide the view from the seat).</summary>
    [Export] public Node3D[] Outside = System.Array.Empty<Node3D>();
    /// <summary>How near the exit a walker must be to board (m).</summary>
    [Export] public float BoardDistance = 2.5f;

    /// <summary>The pilot out of the seat, or null.</summary>
    public Walker Walker { get; private set; }

    private ShipBody _ship;

    public override void _Ready()
    {
        OnFootInput.Register();
        _ship = GetParentOrNull<ShipBody>();
        ShowSeated(true);
    }

    private void ShowSeated(bool seated)
    {
        foreach (Node3D n in Inside)
            if (n != null)
                n.Visible = seated;
        foreach (Node3D n in Outside)
            if (n != null)
                n.Visible = !seated;
    }

    private LandingPad DockedPad =>
        _ship?.Holder is LandingPad { State: LandingPad.PadState.Docked } pad ? pad : null;

    public override void _UnhandledInput(InputEvent e)
    {
        if (Walker != null || Pilot == null || !Pilot.Seated || Pilot.Typing || GameMenu.IsOpen)
            return;
        if (e.IsActionPressed(OnFootInput.LeaveSeat, false, true) && DockedPad is LandingPad pad)
        {
            GetViewport().SetInputAsHandled();
            Leave(pad);
        }
    }

    public override void _Process(double delta)
    {
        if (Pilot == null)
            return;
        if (Walker == null)
        {
            Pilot.Hint = Pilot.Seated && DockedPad != null ? $"DOCKED    {Key(OnFootInput.LeaveSeat)}: leave the seat" : "";
            return;
        }
        if (!IsInstanceValid(Walker))
        {
            Walker = null;
            ShowSeated(true);
            Pilot.Sit();
            return;
        }
        bool near = Walker.GlobalPosition.DistanceTo(ExitPoint) < BoardDistance;
        Walker.Prompt = near ? $"{Key(OnFootInput.Interact)}: board the ship" : "";
    }

    /// <summary>Where the pilot steps out, in the scene.</summary>
    public Vector3 ExitPoint
    {
        get
        {
            if (Exit != null)
                return Exit.GlobalPosition;
            Aabb bounds = _ship.Bounds;
            return _ship.ToGlobal(new Vector3(-1.5f, bounds.Position.Y + 0.2f, bounds.Position.Z - 1.5f));
        }
    }

    /// <summary>Out of the seat, onto the pad beside the ship.</summary>
    public void Leave(LandingPad pad)
    {
        if (WalkerScene == null || Walker != null)
            return;
        Node3D ground = pad.GetParentOrNull<Node3D>() ?? pad;
        Walker = WalkerScene.Instantiate<Walker>();
        ground.AddChild(Walker);
        Walker.GlobalPosition = ExitPoint;
        // Looking along the ship, as the pilot did
        Vector3 ahead = Walker.GlobalBasis.Inverse() * -_ship.GlobalBasis.Z;
        Walker.LookYaw = Mathf.Atan2(-ahead.X, -ahead.Z);
        Walker.Interacted += Board;
        Pilot.Stand();
        ShowSeated(false);
        Walker.TakeControl();
    }

    /// <summary>Back in the seat, from beside the ship.</summary>
    public void Board()
    {
        if (Walker == null || Walker.GlobalPosition.DistanceTo(ExitPoint) >= BoardDistance)
            return;
        Walker.QueueFree();
        Walker = null;
        ShowSeated(true);
        Pilot.Sit();
    }

    private static string Key(string action) => GameSettings.Describe(GameSettings.Binding(action));
}
