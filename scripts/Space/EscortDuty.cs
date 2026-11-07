using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Ship;

namespace Xpace.Space;

/// <summary>Whom a fighter may fight, by whoever gives it its orders: a station's security, an escort's duty.</summary>
public interface IEngagement
{
    IEnumerable<Node3D> Hostiles { get; }
    /// <summary>It is to be looked at, not fired on: it follows the ship and keeps its radar on it.</summary>
    bool Holds(Node3D ship) => false;
}

/// <summary>
/// A ship's orders under attack: an escort fights who attacked what it guards, until they are gone; a cargo ship
/// (<see cref="Fights"/> off) fights nobody and keeps away from them.
/// </summary>
public sealed class EscortDuty : IEngagement
{
    private readonly List<Node3D> _attackers = new();
    private readonly HashSet<Node3D> _inspecting = new();
    private double _quiet;

    public bool Fights = true;

    /// <summary>Who attacked, alive and there.</summary>
    public IEnumerable<Node3D> Attackers => _attackers.Where(a => GodotObject.IsInstanceValid(a) && Combat.IsAlive(a));

    public IEnumerable<Node3D> Hostiles => Fights ? Attackers : Enumerable.Empty<Node3D>();

    public void Add(Node3D attacker)
    {
        if (attacker != null && !_attackers.Contains(attacker))
            _attackers.Add(attacker);
        _quiet = 0;
    }

    /// <summary>It is no longer anyone to fight.</summary>
    public void Forget(Node3D attacker)
    {
        _attackers.Remove(attacker);
        _inspecting.Remove(attacker);
    }

    /// <summary>After this ship, but only to see what it carries, for now.</summary>
    public void Inspect(Node3D ship)
    {
        Add(ship);
        _inspecting.Add(ship);
    }

    /// <summary>What it carries is worth it: it can be fired on.</summary>
    public void Inspected(Node3D ship) => _inspecting.Remove(ship);

    public bool Holds(Node3D ship) => ship != null && _inspecting.Contains(ship);

    /// <summary>The ship it is looking at, if any.</summary>
    public Node3D Inspecting => _inspecting.FirstOrDefault(s => GodotObject.IsInstanceValid(s) && Combat.IsAlive(s));

    /// <summary>Nobody left to fight, for that long now.</summary>
    public bool Over(double step, double seconds)
    {
        _quiet = Attackers.Any() ? 0 : _quiet + step;
        return _quiet >= seconds;
    }
}
