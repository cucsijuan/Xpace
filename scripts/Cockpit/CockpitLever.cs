using Godot;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>What moves a cockpit lever.</summary>
public enum LeverKind
{
    /// <summary>The flight stick: tilts with pitch and roll, twists with yaw.</summary>
    Stick,
    /// <summary>The throttle: slides between its reverse, zero and full positions.</summary>
    Throttle,
}

/// <summary>
/// A control of the cockpit model that moves with the pilot's input. Place it
/// in a cockpit scene at the lever's pivot (the stick's base) and point Part
/// at the model's piece; it is moved under this node when the game starts,
/// and this node turns or slides in its parent's frame (the cockpit's: -Z
/// ahead, +X right). A lever modelled into a bigger mesh is carved out of it
/// instead: CutFrom is that mesh and CutBox, in its own coordinates, holds
/// the lever's triangles. It follows the controls of the ShipBody it ends up
/// under.
/// </summary>
public partial class CockpitLever : Node3D
{
    [Export] public LeverKind Kind;

    /// <summary>The model's piece that moves.</summary>
    [Export] public Node3D Part;

    /// <summary>Or the mesh the lever is part of, and the box around its triangles.</summary>
    [Export] public MeshInstance3D CutFrom;
    [Export] public Aabb CutBox;

    /// <summary>Stick: degrees at full deflection.</summary>
    [Export] public float MaxPitch = 8f;
    [Export] public float MaxRoll = 8f;
    [Export] public float MaxTwist = 8f;

    /// <summary>Throttle: where the lever sits, in metres from where it is modelled.</summary>
    [Export] public Vector3 ZeroOffset;
    [Export] public Vector3 FullOffset = new(0, 0, -0.1f);
    [Export] public Vector3 ReverseOffset = new(0, 0, 0.1f);

    /// <summary>How quickly the lever reaches the input, 1/s (a hand is not instant).</summary>
    [Export] public float Response = 20f;

    private ShipBody _ship;
    private Vector3 _rest;
    private Vector3 _stick;     // pitch, twist (yaw), roll
    private float _throttle;

    public override void _Ready()
    {
        _rest = Position;
        if (CutFrom != null)
            Part = MeshCut.Carve(CutFrom, CutBox, "Lever");
        Part?.Reparent(this, true);
        for (Node n = GetParent(); n != null && _ship == null; n = n.GetParent())
            _ship = n as ShipBody;
        if (_ship == null)
            SetProcess(false);
    }

    public override void _Process(double delta)
    {
        PilotControls c = _ship.Controls;
        float k = 1 - Mathf.Exp(-Response * (float)delta);
        if (Kind == LeverKind.Stick)
        {
            _stick = _stick.Lerp(new Vector3(c.Pitch, c.Yaw, c.Roll), k);
            // Pulling back (pitch up) brings the top toward the pilot (+Z);
            // roll and yaw right lean it and twist it to the right
            Rotation = new Vector3(
                Mathf.DegToRad(MaxPitch) * _stick.X,
                -Mathf.DegToRad(MaxTwist) * _stick.Y,
                -Mathf.DegToRad(MaxRoll) * _stick.Z);
        }
        else
        {
            _throttle = Mathf.Lerp(_throttle, c.Throttle, k);
            Position = _rest + (_throttle >= 0
                ? ZeroOffset.Lerp(FullOffset, _throttle)
                : ZeroOffset.Lerp(ReverseOffset, -_throttle));
        }
    }
}
