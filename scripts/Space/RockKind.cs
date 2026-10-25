using Godot;

namespace Xpace.Space;

/// <summary>
/// One kind of rock a <see cref="RockField"/> is made of: its model, how
/// often it turns up, and the spheres that are its solid shape (what ships
/// hit, what stops rounds and what hides things from radars and seekers).
///
/// The spheres are fitted to the model by the rock shape tool
/// (docs/tools/rocks.md) and kept here, in the model's own units: every
/// machine must collide with the same shape, art or no art.
/// </summary>
[GlobalClass]
public partial class RockKind : Resource
{
    /// <summary>The model: an imported FBX or glTF, or a scene; its first mesh is used.</summary>
    [Export(PropertyHint.File, "*.fbx,*.glb,*.gltf,*.tscn,*.scn,*.res,*.tres")]
    public string Model = "";

    /// <summary>Draws the model with this instead of its own materials (empty: its own).</summary>
    [Export] public Material Material;

    /// <summary>How often this kind turns up, against the others' weights.</summary>
    [Export] public float Weight = 1f;

    /// <summary>The model's size is multiplied by this times the field's scale.</summary>
    [Export] public float Scale = 1f;

    /// <summary>The solid shape: spheres (x, y, z centre, w radius), in the model's frame.</summary>
    [Export] public Vector4[] Spheres = System.Array.Empty<Vector4>();

    /// <summary>Spheres the tool may use at most.</summary>
    [Export] public int MaxSpheres = 10;

    /// <summary>The model's mesh, loaded the first time; a stand-in rock if the model is missing (no art).</summary>
    public Mesh Mesh => _mesh ??= LoadMesh();

    /// <summary>From the model's origin to the farthest point of its spheres.</summary>
    public float Reach
    {
        get
        {
            float reach = 0;
            foreach (Vector4 s in Spheres)
                reach = Mathf.Max(reach, new Vector3(s.X, s.Y, s.Z).Length() + s.W);
            return reach;
        }
    }

    private Mesh _mesh;

    private Mesh LoadMesh()
    {
        Mesh mesh = FindMesh(Model);
        if (mesh == null)
        {
            // No art: a lumpy ball as big as the spheres
            float r = Spheres.Length > 0 ? Reach * 0.8f : 1f;
            mesh = new SphereMesh { Radius = r, Height = 2 * r, RadialSegments = 8, Rings = 5 };
        }
        if (Material != null)
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                mesh.SurfaceSetMaterial(s, Material);
        return mesh;
    }

    /// <summary>The first mesh in a model, a copy (its node's transform is not applied: Synty's rocks have none).</summary>
    public static Mesh FindMesh(string path)
    {
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path))
            return null;
        Resource res = GD.Load(path);
        if (res is Mesh m)
            return (Mesh)m.Duplicate();
        if (res is not PackedScene scene)
            return null;
        Node root = scene.Instantiate();
        Mesh found = null;
        foreach (Node n in root.FindChildren("*", nameof(MeshInstance3D), true, false))
            if (n is MeshInstance3D { Mesh: not null } mi)
            {
                found = (Mesh)mi.Mesh.Duplicate();
                break;
            }
        if (found == null && root is MeshInstance3D { Mesh: not null } self)
            found = (Mesh)self.Mesh.Duplicate();
        root.Free();
        return found;
    }
}
