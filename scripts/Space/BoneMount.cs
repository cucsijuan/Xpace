using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Hangs a model on a bone of an imported skeleton: a child of the model's instance that, when it starts, puts
/// <see cref="Part"/> on <see cref="Bone"/> so it follows it. Synty's character attachments (hair, hats, masks) are
/// modelled in the head bone's frame of the original rig; they go on the bone turned as the body's skin says that frame
/// is (retargeting changes the rest, not the skin), and <see cref="Offset"/> moves them from there. Later, a weapon goes
/// in a hand the same way. It works in the editor too (the nodes it makes are not saved with the scene).
/// </summary>
[Tool]
public partial class BoneMount : Node
{
    private string _bone = "Head";
    private PackedScene _part;
    private Transform3D _offset = Transform3D.Identity;
    private BoneAttachment3D _mount;

    /// <summary>The bone, by its name in the skeleton (a retargeted skeleton has the humanoid profile's names: Head, LeftHand...).</summary>
    [Export]
    public string Bone
    {
        get => _bone;
        set { _bone = value; Remount(); }
    }

    [Export]
    public PackedScene Part
    {
        get => _part;
        set { _part = value; Remount(); }
    }

    /// <summary>From the pose it has in the model to where it hangs, in the model's frame.</summary>
    [Export]
    public Transform3D Offset
    {
        get => _offset;
        set { _offset = value; Remount(); }
    }

    /// <summary>The part's paint (as <see cref="ModelPaint"/> gives the model's own meshes); none keeps the part's.</summary>
    [Export]
    public Material Material
    {
        get => _material;
        set { _material = value; Remount(); }
    }
    private Material _material;

    /// <summary>The part as it hangs (null before it starts or without a part).</summary>
    public Node3D Mounted { get; private set; }

    public override void _Ready() => Mount();

    public override void _ExitTree() => Unmount();

    private void Remount()
    {
        if (!IsInsideTree())
            return;
        Unmount();
        Mount();
    }

    private void Mount()
    {
        if (Part == null || GetParent() is not Node parent)
            return;
        var skeleton = parent.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>().FirstOrDefault();
        int bone = skeleton?.FindBone(Bone) ?? -1;
        if (bone < 0)
            return;
        _mount = new BoneAttachment3D { Name = $"Mount{Name}", BoneName = Bone };
        skeleton.AddChild(_mount);
        Mounted = Part.Instantiate<Node3D>();
        Mounted.Transform = new Transform3D(ModelledIn(skeleton, bone), Vector3.Zero) * Offset;
        _mount.AddChild(Mounted);
        if (Material != null)
            foreach (MeshInstance3D mesh in Mounted.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                                                  .Append(Mounted as MeshInstance3D).Where(m => m != null))
                mesh.MaterialOverride = Material;
    }

    // The part is modelled in the bone's frame as the artist's rig had it. Retargeting changes the bone's rest (the
    // silhouette fix tilts bones, then their axes are reset), but a skinned mesh's bind pose still takes the model's
    // space to the bone's frame: its turn is the part's. With no skinned mesh on the bone, the rest is all there is.
    private static Basis ModelledIn(Skeleton3D skeleton, int bone)
    {
        foreach (MeshInstance3D mesh in skeleton.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            Skin skin = mesh.Skin;
            for (int i = 0; skin != null && i < skin.GetBindCount(); i++)
                if (skin.GetBindBone(i) == bone || skin.GetBindName(i) == skeleton.GetBoneName(bone))
                    return skin.GetBindPose(i).Basis;
        }
        return skeleton.GetBoneGlobalRest(bone).Basis.Inverse();
    }

    private void Unmount()
    {
        if (IsInstanceValid(_mount))
            _mount.QueueFree();
        _mount = null;
        Mounted = null;
    }
}
