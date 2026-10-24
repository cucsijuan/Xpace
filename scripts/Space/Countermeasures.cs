using Godot;
using Xpace.Net;

namespace Xpace.Space;

/// <summary>Something thrown out of a ship that then flies on by itself, straight: chaff, a flare.</summary>
public interface IDropped
{
    Vector3 Velocity { get; set; }

    /// <summary>Seconds since it was thrown out (one made late, for another machine's drop, starts older).</summary>
    float Age { get; set; }
}

/// <summary>Throwing chaff and flares out, here and, in a session, on every machine.</summary>
public static class Countermeasures
{
    /// <summary>A dispenser throws <paramref name="scene"/> out at <paramref name="position"/>, flying at <paramref name="velocity"/>.</summary>
    public static Node3D Drop(Node world, PackedScene scene, Vector3 position, Vector3 velocity)
    {
        Node3D made = Make(world, scene, position, velocity, 0);
        if (SessionNode.Instance is SessionNode { Active: true } session)
            session.Drop(scene, position, velocity);
        return made;
    }

    /// <summary>Makes one, already <paramref name="age"/> seconds old (it was thrown out where it would be by now).</summary>
    public static Node3D Make(Node world, PackedScene scene, Vector3 position, Vector3 velocity, float age)
    {
        var made = scene.Instantiate<Node3D>();
        if (made is IDropped dropped)
        {
            dropped.Velocity = velocity;
            dropped.Age = age;
        }
        world.AddChild(made);
        made.GlobalPosition = position;
        return made;
    }
}
