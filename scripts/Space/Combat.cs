using System;
using Godot;
using Xpace.Net;
using Xpace.Ship;

namespace Xpace.Space;

/// <summary>
/// What happens in a fight, as anyone watching could see it: a ship's
/// weapon goes off (<see cref="Fired"/>: a round, a bolt, a missile leaving
/// its rail), a ship is hit by another's (<see cref="Damaged"/>), a ship is
/// destroyed and by whom (<see cref="Destroyed"/>). A station's security
/// listens (<see cref="StationSecurity"/>) and judges by it. Countermeasures
/// are not weapons. In a session the same is told of every machine's
/// ships (<see cref="SessionNode"/>): a round another machine fired, a hit
/// the host judged, a ship that blew up on its owner's machine.
/// </summary>
public static class Combat
{
    /// <summary>A ship's weapon went off, there.</summary>
    public static event Action<Node3D, Vector3> Fired;
    /// <summary>The first ship was hit by the second's weapon.</summary>
    public static event Action<Node3D, Node3D> Damaged;
    /// <summary>The ship was destroyed there, by the second (null: by nothing known: a rock, its own engine).</summary>
    public static event Action<Node3D, Node3D, Vector3> Destroyed;

    /// <summary>The ship a node belongs to (the node itself, or the nearest ship above it), or null.</summary>
    public static Node3D ShipOf(Node n)
    {
        for (; n != null; n = n.GetParent())
            if (n is ShipBody or Drone or RemoteShip)
                return (Node3D)n;
        return null;
    }

    public static bool IsAlive(Node3D ship) => ship switch
    {
        ShipBody b => !b.Wrecked,
        Drone d => d.Alive,
        RemoteShip r => GodotObject.IsInstanceValid(r) && r.Alive,
        null => false,
        _ => GodotObject.IsInstanceValid(ship),
    };

    /// <summary>A weapon of the shooter's went off at a point.</summary>
    public static void Fire(Node shooter, Vector3 at)
    {
        if (ShipOf(shooter) is Node3D ship)
            Fired?.Invoke(ship, at);
    }

    /// <summary>A weapon of the shooter's hits the victim: the damage, and who did it.</summary>
    public static void Strike(IDamageable victim, float damage, Node shooter, Vector3 at)
    {
        Node3D ship = ShipOf(victim as Node), attacker = ShipOf(shooter);
        bool alive = IsAlive(ship);
        victim.Hit(damage);
        if (ship == null || attacker == ship)
            return;
        if (attacker != null)
            Damaged?.Invoke(ship, attacker);
        if (alive && !IsAlive(ship))
            Destroyed?.Invoke(ship, attacker, at);
    }

    /// <summary>The victim was hit by the shooter's weapon, the damage taken on another machine.</summary>
    public static void Struck(Node victim, Node shooter)
    {
        Node3D ship = ShipOf(victim), attacker = ShipOf(shooter);
        if (ship != null && attacker != null && attacker != ship)
            Damaged?.Invoke(ship, attacker);
    }

    /// <summary>The ship blew up on another machine, there, by the second (null: by nothing known).</summary>
    public static void Lost(Node victim, Node by, Vector3 at)
    {
        Node3D ship = ShipOf(victim), attacker = ShipOf(by);
        if (ship != null)
            Destroyed?.Invoke(ship, attacker == ship ? null : attacker, at);
    }
}
