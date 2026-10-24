using Godot;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>Something in the cockpit the pilot can point at and work with the mouse.</summary>
public interface ICockpitInteractive
{
    /// <summary>The pilot points at it (or no longer does).</summary>
    void Hover(bool on);
    /// <summary>The left button goes down on it.</summary>
    void Press();
    /// <summary>The left button comes up after a Press.</summary>
    void Release();
    /// <summary>The mouse moves, in pixels, while the button is held on it.</summary>
    void Drag(Vector2 pixels);
    /// <summary>Wheel notches over it (up positive).</summary>
    void Wheel(int notches);
}

/// <summary>
/// The base of the cockpit's controls (buttons, sliders, encoders): a part
/// of the model the pilot works with the mouse, wired to a member of
/// something on the ship. Place it in a cockpit scene on the part and point
/// <see cref="Part"/> at the model's piece that moves, or carve it out of a
/// bigger mesh (<see cref="CutFrom"/>, and <see cref="CutBox"/> in that
/// mesh's coordinates). <see cref="Target"/> is a path from the ship the
/// cockpit ends up under (ShipComputer, Rwr/RwrTone, Display...), so the
/// same cockpit works in any ship that has those parts. It glows while the
/// pilot points at it.
/// </summary>
public abstract partial class CockpitControl : Node3D, ICockpitInteractive
{
    /// <summary>The model's piece that moves.</summary>
    [Export] public Node3D Part;

    /// <summary>Or the mesh the part is modelled into, and the box around its triangles.</summary>
    [Export] public MeshInstance3D CutFrom;
    [Export] public Aabb CutBox;

    /// <summary>What it works: a path from the ship (ShipComputer, Rwr/RwrTone, Display...).</summary>
    [Export] public NodePath Target = "";

    /// <summary>Radius of the clickable sphere around this node, metres.</summary>
    [Export] public float Radius = 0.025f;

    [Export] public Color GlowColor = new(1f, 0.85f, 0.2f, 0.8f);
    /// <summary>How much yellow tints the part itself, 0 to 1.</summary>
    [Export] public float GlowTint = 0.45f;
    /// <summary>How far the glow stands out of the part, metres.</summary>
    [Export] public float GlowWidth = 0.0025f;

    protected ShipBody Ship { get; private set; }
    protected Vector3 RestPosition { get; private set; }
    protected Basis RestBasis { get; private set; }

    private Node _target;
    private StandardMaterial3D _glow;

    public override void _Ready()
    {
        RestPosition = Position;
        RestBasis = Basis;
        if (CutFrom != null)
            Part = MeshCut.Carve(CutFrom, CutBox, Name);
        Part?.Reparent(this, true);
        for (Node n = GetParent(); n != null && Ship == null; n = n.GetParent())
            Ship = n as ShipBody;
        // An area, not a body: it rides along with the ship and only answers the pilot's pointing
        var area = new Area3D { Monitoring = false };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius } });
        AddChild(area);
    }

    /// <summary>The node it works, found from the ship once it is there (null if there is none).</summary>
    protected Node TargetNode
    {
        get
        {
            if (_target == null || !IsInstanceValid(_target))
                _target = Ship == null || Target == null || Target.IsEmpty ? null : Ship.GetNodeOrNull(Target);
            return _target;
        }
    }

    public virtual void Hover(bool on)
    {
        // A yellow wash over the part (mixed, so it shows on white too), and a
        // shell round it seen only from inside (its back faces): a rim along the edges
        _glow ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(GlowColor, GlowTint),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NextPass = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = GlowColor,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Front,
                Grow = true,
                GrowAmount = GlowWidth,
            },
        };
        if (Part == null)
            return;
        foreach (Node n in Part.FindChildren("*", "MeshInstance3D", true, false))
            ((MeshInstance3D)n).MaterialOverlay = on ? _glow : null;
        if (Part is MeshInstance3D mesh)
            mesh.MaterialOverlay = on ? _glow : null;
    }

    public virtual void Press() { }
    public virtual void Release() { }
    public virtual void Drag(Vector2 pixels) { }
    public virtual void Wheel(int notches) { }
}
