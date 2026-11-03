using System;
using Godot;

namespace Xpace.Ship;

/// <summary>What a hardpoint takes.</summary>
[Flags]
public enum MountKinds
{
    /// <summary>A weapon fixed along the ship's nose (a laser).</summary>
    Fixed = 1,
    /// <summary>A gimballed turret.</summary>
    Turret = 2,
}

/// <summary>
/// A place on a ship's hull where a weapon is mounted: where it sits and which way it faces (its -Z), how big a weapon
/// it takes (<see cref="Size"/>: a model of a larger size does not fit) and of which kinds (<see cref="Accepts"/>). It
/// is the hull's: what is mounted on it comes from the hull's factory fitting (<see cref="ShipSpec.Fitting"/>, by the
/// hardpoint's node name) or from the pilot's record, and the ship mounts it (<see cref="ShipBody.Fit"/>).
///
/// A laser mounted here shows its model (<see cref="LaserSpec.Scene"/>) and fires from its muzzle (a Marker3D named
/// "Muzzle" in that scene, else the hardpoint itself), as one more emitter of the ship's laser (<see cref="LaserDevice"/>,
/// whose lasers fire in turn). A turret mounted here is the ship's turret (<see cref="TurretDevice"/>, made here). A
/// ship has one turret (one place on the bus): a second one is not mounted.
///
/// In the editor it shows <see cref="Preview"/> where a mounted model would be, to place the hardpoint by eye; the
/// preview is never saved with the scene, nor mounted in the game.
/// </summary>
[Tool]
[GlobalClass]
public partial class Hardpoint : Marker3D
{
    /// <summary>The largest size of weapon it takes (1: a fighter's).</summary>
    [Export] public int Size = 1;
    [Export] public MountKinds Accepts = MountKinds.Fixed;

    /// <summary>What is mounted (a <see cref="LaserSpec"/>, a <see cref="TurretSpec"/>), or null.</summary>
    public Resource Mounted { get; private set; }
    /// <summary>Where a laser mounted here fires from (along its -Z), or null.</summary>
    public Node3D Emitter { get; private set; }
    /// <summary>The turret mounted here, or null.</summary>
    public TurretDevice Turret { get; private set; }

    private Node _content;
    private Resource _preview;
    private Node _previewNode;

    /// <summary>The editor only: a model (a laser's or a turret's spec) to show here while placing the hardpoint.</summary>
    [Export(PropertyHint.ResourceType, "LaserSpec,TurretSpec")]
    public Resource Preview
    {
        get => _preview;
        set
        {
            _preview = value;
            ShowPreview();
        }
    }

    public override void _Ready() => ShowPreview();

    // The preview's look, as an internal child: the scene file never gets it. Read through Get: in the editor the
    // specs' scripts are not loaded, only their properties
    private void ShowPreview()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;
        _previewNode?.QueueFree();
        _previewNode = null;
        if (_preview?.Get("Scene").AsGodotObject() is PackedScene scene && scene.Instantiate() is Node look)
        {
            _previewNode = look;
            AddChild(look, false, InternalMode.Back);
        }
    }

    /// <summary>Whether <paramref name="model"/> may be mounted here: its kind and its size.</summary>
    public bool Takes(Resource model) => model switch
    {
        null => true,
        LaserSpec laser => (Accepts & MountKinds.Fixed) != 0 && laser.Size <= Size,
        TurretSpec turret => (Accepts & MountKinds.Turret) != 0 && turret.Size <= Size,
        _ => false,
    };

    /// <summary>
    /// Mounts <paramref name="model"/> here in place of what was (null: nothing). One it doesn't take is not mounted.
    /// <paramref name="working"/> false mounts only its look: another machine's ship, which shows what is mounted and
    /// works nothing (a turret's look stays pointing ahead).
    /// </summary>
    public void Mount(Resource model, bool working = true)
    {
        if (model == Mounted && (model == null || _content != null))
            return;
        if (_content != null)
        {
            RemoveChild(_content);
            _content.QueueFree();
        }
        _content = null;
        Emitter = null;
        Turret = null;
        Mounted = Takes(model) ? model : null;
        switch (Mounted)
        {
            case LaserSpec laser:
                var look = laser.Scene?.Instantiate<Node3D>() ?? new Node3D();
                look.Name = "Laser";
                AddChild(look);
                _content = look;
                Emitter = look.GetNodeOrNull<Node3D>("Muzzle") ?? look;
                break;
            case TurretSpec turret when working:
                Turret = new TurretDevice { Name = "Turret", Spec = turret };
                AddChild(Turret);
                _content = Turret;
                break;
            case TurretSpec turret:
                _content = turret.Scene?.Instantiate() ?? new Node3D();
                _content.Name = "Turret";
                AddChild(_content);
                break;
        }
    }
}
