using System;
using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>One radar emitter as a warning receiver hears it for a step.</summary>
public sealed class Ping
{
    public IRadarEmitter Emitter;
    public Node Node;
    /// <summary>The ship that carries it, if it is on one: a ship does not hear its own radar.</summary>
    public Xpace.Ship.ShipBody Ship;
    public Vector3 Position;
    public float PowerAt1Km;
    public int Order;
}

/// <summary>
/// Every radar emitter of a sector, taken once a step, in a spatial grid: a warning receiver asks who is loud enough to
/// reach it. An emitter's signal falls with the square of the distance, so it reaches as far as the root of its power.
/// </summary>
public sealed class EmitterPicture
{
    private readonly SpatialGrid<Ping> _grid = new(5000f);
    private readonly List<Ping> _near = new();
    private int _count;

    public static EmitterPicture Of(Node from) => Sectors.Built(from, "radar_emitters", typeof(EmitterPicture), nodes =>
    {
        var picture = new EmitterPicture();
        foreach (Node node in nodes)
        {
            if (node is not IRadarEmitter e || !e.Stand())
                continue;
            Xpace.Ship.ShipBody carrier = null;
            for (Node up = node; up != null && carrier == null; up = up.GetParent())
                carrier = up as Xpace.Ship.ShipBody;
            var ping = new Ping { Emitter = e, Node = node, Ship = carrier, Position = e.GlobalPosition, PowerAt1Km = e.PowerAt1Km, Order = picture._count++ };
            picture._grid.Add(ping, ping.Position, MathF.Sqrt(Math.Max(ping.PowerAt1Km, 0)));
        }
        return picture;
    });

    /// <summary>
    /// The emitters whose signal reaches a receiver at a point with at least <paramref name="floor"/> (signal to noise,
    /// linear) given its <paramref name="sensitivity"/>: power x sensitivity / km² >= floor. In the order of the list.
    /// </summary>
    public List<Ping> Reaching(Vector3 at, float sensitivity, float floor)
    {
        _near.Clear();
        _grid.Reaching(at, 1000f * MathF.Sqrt(sensitivity / floor), _near);
        _near.Sort((a, b) => a.Order.CompareTo(b.Order));
        return _near;
    }
}
