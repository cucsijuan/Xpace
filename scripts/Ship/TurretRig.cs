using Godot;

namespace Xpace.Ship;

/// <summary>
/// A turret's model and how it moves: what turns (<see cref="Yoke"/>, about Y),
/// what elevates (<see cref="Cradle"/>, about X), what spins while it fires,
/// and where rounds leave. Put it at the root of a scene with the model under
/// it (scenes/weapons/turrets), named in <see cref="TurretSpec.Scene"/>. Scale
/// the root to fit a hardpoint's size. It looks along -Z, as a ship does.
/// </summary>
public partial class TurretRig : Node3D
{
    [Export] public NodePath YokePath = "Yoke";
    /// <summary>Elevates, about its own X. Often a node inside the imported model.</summary>
    [Export] public NodePath CradlePath;
    /// <summary>Spins about its own Z while firing (a gatling's barrels); none: nothing spins.</summary>
    [Export] public NodePath BarrelPath;
    /// <summary>Where rounds leave, in the cradle's frame.</summary>
    [Export] public Vector3 MuzzleAt;
    /// <summary>The model faces +Z, as Synty's do, turned half a turn under the yoke: in its cradle's frame, ahead and up go the other way.</summary>
    [Export] public bool FacesPlusZ;
    /// <summary>The barrels' spin at full fire, radians a second.</summary>
    [Export] public float BarrelSpin = 30f;
    [Export] public float BarrelSpinUp = 40f;

    public Node3D Yoke { get; private set; }
    public Node3D Cradle { get; private set; }
    public Node3D Muzzle { get; private set; }

    private Node3D _barrel;
    private float _spin;

    public override void _Ready()
    {
        Yoke = GetNode<Node3D>(YokePath);
        Cradle = GetNode<Node3D>(CradlePath);
        _barrel = BarrelPath == null || BarrelPath.IsEmpty ? null : GetNodeOrNull<Node3D>(BarrelPath);
        Muzzle = new Node3D { Name = "Muzzle", Position = MuzzleAt, RotationDegrees = FacesPlusZ ? new Vector3(0, 180, 0) : Vector3.Zero };
        Cradle.AddChild(Muzzle);
    }

    /// <summary>Points it: radians, right and up positive.</summary>
    public void Aim(float azimuth, float elevation)
    {
        Yoke.Rotation = new Vector3(0, -azimuth, 0);
        Cradle.Rotation = new Vector3(FacesPlusZ ? -elevation : elevation, 0, 0);
    }

    /// <summary>Each step: the barrels wind up while it fires, and run down after.</summary>
    public void Spin(bool firing, float dt)
    {
        if (_barrel == null)
            return;
        _spin = Mathf.MoveToward(_spin, firing ? BarrelSpin : 0, BarrelSpinUp * dt);
        _barrel.RotateObjectLocal(Vector3.Forward, _spin * dt);
    }
}
