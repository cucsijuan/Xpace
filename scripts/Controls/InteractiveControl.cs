using Godot;
using Xpace.Cockpit;

namespace Xpace.Controls;

/// <summary>
/// What the controls are wired to: a ship, a station... Something whose parts
/// (systems, devices) the controls' <see cref="InteractiveControl.Target"/>
/// paths start from, so a panel works wherever it is placed.
/// </summary>
public interface IControlHost
{
}

/// <summary>Something the player can point at and work with the mouse.</summary>
public interface IInteractive
{
    /// <summary>The player points at it (or no longer does).</summary>
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
/// The base of the controls (buttons, sliders, switches, encoders): the root
/// of a control's scene, with the piece that moves (<see cref="Part"/>)
/// below it. The control moves only that piece, in its own frame: a
/// slider's knob along the scene's Y, a switch's lever about its X. A piece
/// modelled into a bigger mesh can still be carved out of it at run time
/// (<see cref="CutFrom"/>, <see cref="CutBox"/> in that mesh's coordinates),
/// but a scene of its own is better (tools/mesh_parts).
///
/// <see cref="Target"/> is what it works: a path from the
/// <see cref="IControlHost"/> it ends up under (the ship: ShipComputer,
/// Rwr/RwrTone, Display...), so a panel built once works in any ship or
/// station with those parts. It glows while pointed at. See
/// docs/controls.md.
/// </summary>
public abstract partial class InteractiveControl : Node3D, IInteractive
{
    /// <summary>The piece that moves.</summary>
    [Export] public Node3D Part;

    /// <summary>Or the mesh the piece is modelled into, and the box around its triangles.</summary>
    [Export] public MeshInstance3D CutFrom;
    [Export] public Aabb CutBox;

    /// <summary>What it works: a path from the host (the ship).</summary>
    [Export] public NodePath Target = "";

    /// <summary>Radius of the clickable sphere around this node, metres.</summary>
    [Export] public float Radius = 0.025f;

    [ExportGroup("Glow")]
    [Export] public Color GlowColor = new(1f, 0.85f, 0.2f, 0.8f);
    /// <summary>How much yellow washes over the piece, 0 to 1.</summary>
    [Export] public float GlowTint = 0.45f;
    /// <summary>How far the rim stands out of the piece, metres.</summary>
    [Export] public float GlowWidth = 0.0025f;

    protected Node Host { get; private set; }
    /// <summary>The piece's transform as modelled (its parent's frame).</summary>
    protected Transform3D PartRest { get; private set; } = Transform3D.Identity;

    private Node _target;
    private StandardMaterial3D _glow;

    public override void _Ready()
    {
        if (CutFrom != null)
            Part = MeshCut.Carve(CutFrom, CutBox, Name);
        if (Part != null && Part.GetParent() != this)
            Part.Reparent(this, true);
        if (Part != null)
            PartRest = Part.Transform;
        for (Node n = GetParent(); n != null && Host == null; n = n.GetParent())
            if (n is IControlHost)
                Host = n;
        // An area, not a body: it rides along with its host and only answers the pointing
        var area = new Area3D { Monitoring = false };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius } });
        AddChild(area);
    }

    /// <summary>The node it works, found from the host once it is there (null if there is none).</summary>
    protected Node TargetNode
    {
        get
        {
            if (_target == null || !IsInstanceValid(_target))
                _target = Host == null || Target == null || Target.IsEmpty ? null : Host.GetNodeOrNull(Target);
            return _target;
        }
    }

    /// <summary>Puts the piece at its modelled place moved by this (in its parent's frame).</summary>
    protected void MovePart(Transform3D offset)
    {
        if (Part != null)
            Part.Transform = offset * PartRest;
    }

    public virtual void Hover(bool on)
    {
        // A yellow wash over the piece (mixed, so it shows on white too), and a
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
