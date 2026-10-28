using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Space;

namespace Xpace.OnFoot;

/// <summary>
/// What people on foot stand on and bump into: a static body on <see cref="Layer"/>, made when it starts from the
/// cheapest surface at hand. Under a <see cref="Solid"/> it takes the solid's triangles (the same coarse collision
/// meshes ships meet); otherwise <see cref="Shape"/>'s meshes, or, with no shape, the parent's own meshes (a landing
/// pad's platform: a low-poly model is its own good enough collision); and the box shapes under the parent (a ship's
/// hitboxes: boxes keep walkers off the hull). Ships, rounds and radar never see it.
/// </summary>
[GlobalClass]
public partial class WalkSurface : Node3D
{
    /// <summary>The physics layer of everything walkers meet (layer 5); the camera's arm stops on it too.</summary>
    public const uint Layer = 16;

    /// <summary>A model whose meshes are the surface; none takes the parent solid's triangles, or the parent's meshes.</summary>
    [Export] public PackedScene Shape;

    /// <summary>Also the box shapes under the parent (hitboxes, a solid's boxes).</summary>
    [Export] public bool Boxes = true;

    public StaticBody3D Body { get; private set; }

    public override void _Ready()
    {
        var faces = new List<Vector3>();
        Transform3D toLocal = GlobalTransform.AffineInverse();
        Node parent = GetParent();
        if (Shape != null)
        {
            Node model = Shape.Instantiate();
            Meshes(model, GlobalTransform, faces, toLocal);
            model.Free();
        }
        else if (parent is Solid solid)
        {
            if (solid.A.Length == 0)
                solid.Bake();                   // children start before their parent
            Transform3D frame = toLocal * solid.Frame;
            for (int i = 0; i < solid.A.Length; i++)
            {
                faces.Add(frame * solid.A[i]);
                faces.Add(frame * solid.B[i]);
                faces.Add(frame * solid.C[i]);
            }
        }
        else if (parent is Node3D owner)
            foreach (Node child in owner.GetChildren())
                if (child != this)
                    Meshes(child, owner.GlobalTransform, faces, toLocal);

        Body = new StaticBody3D { Name = "Body", CollisionLayer = Layer, CollisionMask = 0 };
        AddChild(Body);
        if (faces.Count > 0)
            Body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces.ToArray(), BackfaceCollision = true } });
        if (Boxes && parent != null && parent is not Solid)
            foreach (CollisionShape3D box in parent.FindChildren("*", nameof(CollisionShape3D), true, false).OfType<CollisionShape3D>())
                if (box.Shape is BoxShape3D && !IsAncestorOf(box))
                    Body.AddChild(new CollisionShape3D { Shape = box.Shape, Transform = toLocal * box.GlobalTransform });
    }

    private static void Meshes(Node node, Transform3D parent, List<Vector3> faces, Transform3D toLocal)
    {
        Transform3D at = node is Node3D n3 ? parent * n3.Transform : parent;
        if (node is MeshInstance3D { Mesh: not null } mesh)
        {
            Transform3D to = toLocal * at;
            foreach (Vector3 v in mesh.Mesh.GetFaces())
                faces.Add(to * v);
        }
        foreach (Node child in node.GetChildren())
            Meshes(child, at, faces, toLocal);
    }
}
