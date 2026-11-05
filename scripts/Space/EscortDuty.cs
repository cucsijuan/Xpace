using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Ship;

namespace Xpace.Space;

/// <summary>Whom a fighter may fight, by whoever gives it its orders: a station's security, an escort's duty.</summary>
public interface IEngagement
{
    IEnumerable<Node3D> Hostiles { get; }
}

/// <summary>
/// A ship's orders under attack: an escort fights who attacked what it guards, until they are gone; a cargo ship
/// (<see cref="Fights"/> off) fights nobody and keeps away from them.
/// </summary>
public sealed class EscortDuty : IEngagement
{
    private readonly List<Node3D> _attackers = new();
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
    public void Forget(Node3D attacker) => _attackers.Remove(attacker);

    /// <summary>Nobody left to fight, for that long now.</summary>
    public bool Over(double step, double seconds)
    {
        _quiet = Attackers.Any() ? 0 : _quiet + step;
        return _quiet >= seconds;
    }
}
