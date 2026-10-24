using System;
using Godot;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// On the ship's owner: a snapshot of the ship every
/// <see cref="IntervalTicks"/> sector ticks (3 = 20 a second), for whoever
/// carries it to the other machines. Place it under the ShipBody.
/// </summary>
public partial class ShipStateSender : Node
{
    [Export] public int IntervalTicks = 3;

    /// <summary>A snapshot's bytes, ready to send unreliably.</summary>
    public event Action<byte[]> Packet;

    private ShipBody _ship;
    private FloatingOrigin _origin;

    public override void _Ready()
    {
        _ship = GetParent<ShipBody>();
        for (Node n = _ship; n != null && _origin == null; n = n.GetParent())
            _origin = n as FloatingOrigin;
        ProcessPhysicsPriority = 50;            // after the ship has moved, before the origin shifts
    }

    public override void _PhysicsProcess(double delta)
    {
        uint tick = SectorClock.Instance?.Tick ?? 0;
        if (tick % (uint)IntervalTicks != 0)
            return;
        ShipState s = _ship.State;
        var snapshot = new ShipSnapshot
        {
            Tick = tick,
            Position = (_origin?.Origin ?? default) + s.Position,
            Orientation = s.Orientation,
            Velocity = s.Velocity,
            AngularVelocity = s.AngularVelocity,
        };
        Packet?.Invoke(snapshot.ToBytes());
    }
}
