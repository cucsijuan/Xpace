using System.Collections.Generic;
using Godot;
using Xpace.Ship;

namespace Xpace.Space;

/// <summary>
/// A jump gate: a ring that sends what goes through it to the gate
/// <see cref="ExitGate"/> of the sector <see cref="Destination"/>. Its
/// frame: the ring's disc in its XY plane, centred, <see cref="Radius"/>
/// across; ships go through along Z, either way.
///
/// The gate's radio (a <see cref="StationRadio"/> child, with the laws and
/// the toll) clears a ship that asks (<see cref="Clear"/>). The ring does
/// not know who passes: every ship that goes through jumps, cleared or not,
/// and one without clearance has broken the gate's law (its
/// <see cref="StationSecurity"/> judges it, on the machine that judges).
///
/// The player's ship, going through on the machine that flies it, is taken
/// to the other sector (<see cref="SectorHost.Jump"/>), held in transit for
/// <see cref="TransitSeconds"/> with <see cref="TunnelScene"/> round it,
/// and let go past the exit gate with the velocity it came in with, turned
/// from this gate's frame into that one's.
/// </summary>
public partial class JumpGate : Node3D
{
    /// <summary>The Id of the sector it leads to.</summary>
    [Export] public string Destination = "";
    /// <summary>The name of the gate there that ships come out of.</summary>
    [Export] public string ExitGate = "";
    /// <summary>The radius of the ring's opening, metres.</summary>
    [Export] public float Radius = 57f;
    [Export] public double TransitSeconds = 4;
    /// <summary>How far past the exit gate's ring a ship comes out (more than a ship is long).</summary>
    [Export] public float ExitMargin = 60f;
    /// <summary>What a ship in transit is surrounded by; its -Z is the way it goes.</summary>
    [Export] public PackedScene TunnelScene;

    /// <summary>The radio that speaks for it.</summary>
    public StationRadio Radio { get; private set; }

    /// <summary>Where it leads, as players see it.</summary>
    public string Leads => SectorHost.Current?.Map?.Find(Destination)?.Title ?? Destination;

    private readonly Dictionary<Node3D, double> _cleared = new();
    private readonly Dictionary<Node3D, Vector3> _was = new();     // each ship's place in the gate's frame last step
    private double _now;

    public override void _Ready()
    {
        foreach (Node n in GetChildren())
            Radio ??= n as StationRadio;
        ProcessPhysicsPriority = 60;            // after the ships have moved, before the origin shifts
    }

    /// <summary>The radio cleared a ship to go through, for so many seconds.</summary>
    public void Clear(Node3D ship, double seconds)
    {
        if (ship != null)
            _cleared[ship] = _now + seconds;
    }

    public bool Cleared(Node3D ship) => ship != null && _cleared.TryGetValue(ship, out double until) && until >= _now;

    public override void _PhysicsProcess(double delta)
    {
        _now += delta;
        var seen = new HashSet<Node3D>();
        foreach (Node node in Sectors.InGroup(this, Space.Radio.Group))
        {
            if (Combat.ShipOf(node) is not Node3D ship || !seen.Add(ship) || node == Radio)
                continue;
            Vector3 now = ToLocal(ship.GlobalPosition);
            if (_was.TryGetValue(ship, out Vector3 was) && Mathf.Sign(was.Z) != Mathf.Sign(now.Z) && was.Z != 0)
            {
                Vector3 at = was.Lerp(now, was.Z / (was.Z - now.Z));
                if (at.X * at.X + at.Y * at.Y <= Radius * Radius)
                    Through(ship, at);
            }
            _was[ship] = now;
        }
        foreach (Node3D gone in new List<Node3D>(_was.Keys))
            if (!seen.Contains(gone))
                _was.Remove(gone);
        foreach (Node3D ship in new List<Node3D>(_cleared.Keys))
            if (!IsInstanceValid(ship) || _cleared[ship] < _now)
                _cleared.Remove(ship);
    }

    // A ship went through the ring at a point of it (the gate's frame)
    private void Through(Node3D ship, Vector3 at)
    {
        if (!_cleared.Remove(ship))
            Radio?.Security?.UnpaidTransit(ship);
        if (ship is ShipBody body && body == SectorHost.Current?.Ship && !body.Held)
            Transit(body, at);
    }

    // The player's ship to the other sector, out of the exit gate as it came in here
    private void Transit(ShipBody ship, Vector3 at)
    {
        SectorHost host = SectorHost.Current;
        if (host.Map?.Find(Destination) == null)
        {
            GD.PushWarning($"gate {Name}: no sector '{Destination}' in the map");
            return;
        }
        Basis entry = GlobalBasis.Orthonormalized();
        Basis turn = entry.Inverse() * ship.GlobalBasis.Orthonormalized();
        Vector3 velocity = entry.Inverse() * ship.LinearVelocity;
        Vector3 spin = entry.Inverse() * ship.AngularVelocity;
        host.Jump(Destination, ship.GlobalTransform);

        if (host.Sector.FindChild(ExitGate, true, false) is not JumpGate exit)
        {
            GD.PushWarning($"gate {Name}: no gate '{ExitGate}' in sector '{Destination}'");
            return;
        }
        Basis outward = exit.GlobalBasis.Orthonormalized();
        float side = velocity.Z > 0 ? 1 : -1;
        var pose = new Transform3D(outward * turn, exit.ToGlobal(new Vector3(at.X, at.Y, side * exit.ExitMargin)));
        Node3D tunnel = TunnelScene?.Instantiate<Node3D>();
        if (tunnel != null)
        {
            Vector3 way = (turn.Inverse() * velocity).Normalized();
            ship.AddChild(tunnel);
            tunnel.Basis = way.LengthSquared() > 0.5f && Mathf.Abs(way.Dot(Vector3.Up)) < 0.99f
                ? Basis.LookingAt(way, Vector3.Up) : Basis.Identity;
        }
        ship.GlobalTransform = pose;
        ship.LinearVelocity = Vector3.Zero;
        ship.AngularVelocity = Vector3.Zero;
        ship.Holder = new GateTransit(pose, outward * velocity, outward * spin, TransitSeconds, tunnel);
    }
}

/// <summary>
/// A ship in a gate's tunnel: still, past the exit gate, its computer running;
/// when the time is up it goes on with the velocity it had going in.
/// </summary>
public sealed class GateTransit : IShipHold
{
    private readonly Transform3D _pose;
    private readonly Vector3 _velocity, _spin;
    private readonly Node3D _tunnel;
    private double _left;

    public GateTransit(Transform3D pose, Vector3 velocity, Vector3 spin, double seconds, Node3D tunnel)
    {
        _pose = pose;
        _velocity = velocity;
        _spin = spin;
        _left = seconds;
        _tunnel = tunnel;
    }

    /// <summary>Seconds still in the tunnel.</summary>
    public double Left => _left;

    /// <summary>The velocity it goes on with (the one it went in with, in the exit gate's frame).</summary>
    public Vector3 Velocity => _velocity;

    public ShipState Hold(ShipBody ship, double delta)
    {
        _left -= delta;
        var state = new ShipState { Position = _pose.Origin, Orientation = _pose.Basis.GetRotationQuaternion() };
        if (_left > 0)
            return state;
        ship.Holder = null;
        if (GodotObject.IsInstanceValid(_tunnel))
            _tunnel.QueueFree();
        state.Velocity = _velocity;
        state.AngularVelocity = _spin;
        return state;
    }
}
