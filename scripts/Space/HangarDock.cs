using Godot;

namespace Xpace.Space;

/// <summary>
/// A dock a station's traffic control can give a ship: a node at the
/// middle of a hangar's door, its -Z into the hangar. Cleared
/// (<see cref="Clear"/>), it opens its door (a <see cref="MeshSlide"/>,
/// slowly) and shows its beacon (lights round the door and a beam out of
/// it, along +Z) for the ship to follow in; the door stops ships until it
/// is nearly open (<see cref="DoorSolid"/>). Closed again
/// (<see cref="Close"/>), the beacon goes out and the door comes down.
///
/// The station finds its docks itself (every one below it), and numbers them
/// by <see cref="Number"/> (0: in the order they come).
///
/// In a session the host's station decides: its docks opening and shutting
/// (<see cref="Changed"/>) are shown on every machine (<see cref="Show"/>).
/// </summary>
public partial class HangarDock : Node3D
{
    [Export] public int Number;
    [Export] public MeshSlide Door;
    /// <summary>What blocks the doorway while the door is not open.</summary>
    [Export] public Solid DoorSolid;
    [Export] public Node3D Beacon;
    /// <summary>How open the door must be for ships to pass.</summary>
    [Export(PropertyHint.Range, "0,1")] public float PassableAt = 0.9f;
    /// <summary>Where a ship is inside (this node's frame).</summary>
    [Export] public Vector3 Inside = new(0, -3, -45);
    /// <summary>Where a ship lines up outside, before going in (this node's frame).</summary>
    [Export] public Vector3 Approach = new(0, 0, 250);
    /// <summary>How near <see cref="Inside"/> a ship counts as in the hangar, metres.</summary>
    [Export] public float InsideRadius = 50f;

    /// <summary>Whom it is cleared for (null: free).</summary>
    public Node ClearedFor { get; private set; }

    /// <summary>The station whose dock it is (set by the station).</summary>
    public StationRadio Station { get; set; }

    /// <summary>Open (its door going up, its beacon lit) or shut.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>A dock was cleared or closed here (not when only shown).</summary>
    public static event System.Action<HangarDock> Changed;

    public Vector3 InsidePoint => GlobalTransform * Inside;
    public Vector3 ApproachPoint => GlobalTransform * Approach;

    public override void _Ready()
    {
        if (Beacon != null)
            Beacon.Visible = IsOpen;
    }

    public void Clear(Node ship)
    {
        ClearedFor = ship;
        Show(true);
        Changed?.Invoke(this);
    }

    public void Close()
    {
        ClearedFor = null;
        Show(false);
        Changed?.Invoke(this);
    }

    /// <summary>Opens or shuts it as another machine's station did (whom for is that station's business).</summary>
    public void Show(bool open)
    {
        IsOpen = open;
        if (Door != null)
            Door.Open = open ? 1 : 0;
        if (Beacon != null)
            Beacon.Visible = open;
    }

    public bool Holds(Node3D ship) => ship != null && ship.GlobalPosition.DistanceTo(InsidePoint) < InsideRadius;

    public override void _PhysicsProcess(double delta)
    {
        if (DoorSolid != null)
            DoorSolid.Blocks = Door == null || Door.At < PassableAt;
    }
}
