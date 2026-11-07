using System;
using System.Collections.Generic;
using System.Linq;
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
///
/// Whatever destroys a ship, a weapon, a crash, its own engine, its death is
/// told once, where it happens (<see cref="Down"/>: its hull, a drone, a
/// ship of another machine), and blamed on the last ship that struck it
/// within <see cref="BlameSeconds"/>: the one whose round finished it, or the
/// one that rammed it into a wreck. Nobody's doing, if none did.
/// </summary>
public static class Combat
{
    /// <summary>A ship's weapon went off, there.</summary>
    public static event Action<Node3D, Vector3> Fired;
    /// <summary>The first ship was hit by the second's weapon.</summary>
    public static event Action<Node3D, Node3D> Damaged;
    /// <summary>The ship was destroyed there, by the second (null: by nothing known: a rock, its own engine).</summary>
    public static event Action<Node3D, Node3D, Vector3> Destroyed;

    /// <summary>How long a blow is held against the ship that struck it, if the ship struck is destroyed (s).</summary>
    public const double BlameSeconds = 10;

    // The last ship to strike each ship, and when (physics frame)
    private static readonly Dictionary<Node3D, (Node3D By, ulong Frame)> _blows = new();

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

    /// <summary>A weapon of the shooter's hits the victim: the damage, and who did it. If it destroys the ship, the ship tells it.</summary>
    public static void Strike(IDamageable victim, float damage, Node shooter, Vector3 at)
    {
        Struck(victim as Node, shooter);
        victim.Hit(damage);
    }

    /// <summary>
    /// The victim was struck by another ship: by its weapon, the damage taken on another machine, or by the ship
    /// itself running into it.
    /// </summary>
    public static void Struck(Node victim, Node by)
    {
        Node3D ship = ShipOf(victim), attacker = ShipOf(by);
        if (ship == null || attacker == null || attacker == ship)
            return;
        _blows[ship] = (attacker, Engine.GetPhysicsFrames());
        Damaged?.Invoke(ship, attacker);
    }

    /// <summary>The last ship to strike this one within <see cref="BlameSeconds"/>, if it is still there.</summary>
    public static Node3D Blame(Node ship)
    {
        if (ShipOf(ship) is not Node3D s || !_blows.TryGetValue(s, out var blow))
            return null;
        ulong since = Engine.GetPhysicsFrames() - blow.Frame;
        return since <= BlameSeconds * Engine.PhysicsTicksPerSecond && GodotObject.IsInstanceValid(blow.By) ? blow.By : null;
    }

    /// <summary>The ship was destroyed there, here or on the machine that flies it: by whoever struck it last.</summary>
    public static void Down(Node victim, Vector3 at)
    {
        if (ShipOf(victim) is not Node3D ship)
            return;
        Node3D by = Blame(ship);
        _blows.Remove(ship);
        Destroyed?.Invoke(ship, by, at);
        foreach (Node3D gone in _blows.Keys.Where(k => !GodotObject.IsInstanceValid(k)).ToList())
            _blows.Remove(gone);                    // ships freed since
    }
}
