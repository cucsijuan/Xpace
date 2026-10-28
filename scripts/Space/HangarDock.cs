using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A dock a station's traffic control can give a ship: a node at the
/// middle of a hangar's door, its -Z into the hangar. Opened
/// (<see cref="Use"/>), its door goes up (a <see cref="MeshSlide"/>,
/// slowly) and its beacon shows (lights round the door and a beam out of
/// it, along +Z) for ships to follow in; the door stops ships until it is
/// nearly open (<see cref="DoorSolid"/>). Shut, the beacon goes out and
/// the door comes down.
///
/// The hangar's landing pads (<see cref="LandingPad"/>, anywhere in the
/// hangar's scene, numbered by <see cref="LandingPad.Number"/>, 0: in the
/// order they come) take ships in, one to a pad: the station clears a ship
/// to the hangar and one of its pads. A hangar with no pads takes one ship
/// at a time, and holds none.
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
    [Export] public float InsideRadius = 60f;

    /// <summary>Whom it is cleared for (null: free).</summary>
    public Node ClearedFor { get; private set; }

    /// <summary>The station whose dock it is (set by the station).</summary>
    public StationRadio Station { get; set; }

    /// <summary>Open (its door going up, its beacon lit) or shut.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Its door opened or shut here (not when only shown).</summary>
    public static event System.Action<HangarDock> Changed;

    /// <summary>Its landing pads (any below the hangar's scene root, its parent).</summary>
    public IReadOnlyList<LandingPad> Pads => _pads;

    private List<LandingPad> _pads = new();

    public Vector3 InsidePoint => GlobalTransform * Inside;
    public Vector3 ApproachPoint => GlobalTransform * Approach;

    public override void _Ready()
    {
        if (Beacon != null)
            Beacon.Visible = IsOpen;
        Node hangar = GetParent() ?? this;
        _pads = hangar.FindChildren("*", "", true, false).OfType<LandingPad>().ToList();
        for (int i = 0; i < _pads.Count; i++)
            if (_pads[i].Number == 0)
                _pads[i].Number = i + 1;
        _pads.Sort((a, b) => a.Number.CompareTo(b.Number));
    }

    /// <summary>Its pad of that number, or null.</summary>
    public LandingPad PadNumbered(int number) => _pads.FirstOrDefault(p => p.Number == number);

    /// <summary>
    /// What the station's traffic control makes of it: whom it is held for
    /// alone (a ship of the station's own, or the one ship of a hangar with
    /// no pads; null: none), and whether the door is open.
    /// </summary>
    public void Use(Node clearedFor, bool open)
    {
        ClearedFor = clearedFor;
        if (open == IsOpen)
            return;
        Show(open);
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
