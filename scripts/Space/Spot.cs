using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A place in a sector, kept for later. The world's origin moves under everything as a ship flies
/// (<see cref="FloatingOrigin"/>), so a world position kept in a <see cref="Vector3"/> is wrong after the next shift.
/// A spot keeps where the place is in the sector, in double precision, and <see cref="World"/> says where that is
/// now. Nobody has to be told of a shift.
///
/// The rule: anything that keeps a world position past the step it was worked out in keeps a spot. A
/// <see cref="Vector3"/> field is for directions, speeds, what is relative to a node, and what is worked out again
/// every step. tests/places_test checks every field in the game against this.
/// </summary>
public readonly struct Spot
{
    private readonly FloatingOrigin _root;
    private readonly SectorPoint _at;

    private Spot(FloatingOrigin root, SectorPoint at)
    {
        _root = root;
        _at = at;
    }

    /// <summary>The spot at a world position now, in the sector of a node (one in the tree).</summary>
    public static Spot At(Node where, Vector3 world)
    {
        FloatingOrigin root = FloatingOrigin.Of(where);
        return new Spot(root, (root?.Origin ?? default) + world);
    }

    /// <summary>Where it is in the world now.</summary>
    public Vector3 World => (GodotObject.IsInstanceValid(_root) ? _root.Origin : default).To(_at);

    /// <summary>The spot this far from here.</summary>
    public Spot Moved(Vector3 by) => new(_root, _at + by);

    public float DistanceTo(Vector3 world) => World.DistanceTo(world);

    public override string ToString() => _at.ToString();
}

/// <summary>A queue of spots to go through, in world positions for whoever uses it.</summary>
public sealed class Spots
{
    private readonly Node _owner;
    private readonly Queue<Spot> _spots = new();

    public Spots(Node owner) => _owner = owner;

    public int Count => _spots.Count;

    public void Enqueue(Vector3 world) => _spots.Enqueue(Spot.At(_owner, world));

    public Vector3 Peek() => _spots.Peek().World;

    public Vector3 Dequeue() => _spots.Dequeue().World;

    public void Clear() => _spots.Clear();
}
