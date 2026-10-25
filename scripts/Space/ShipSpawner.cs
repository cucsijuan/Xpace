using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.Ship;

namespace Xpace.Space;

/// <summary>
/// Brings ships in while playing: keeps up to <see cref="Count"/> of
/// <see cref="Scene"/> alive round this node, the first after
/// <see cref="FirstSeconds"/>, then one every <see cref="IntervalSeconds"/>
/// while fewer are alive. Each appears somewhere within
/// <see cref="Radius"/>, facing any way (from <see cref="Seed"/>: the same
/// every game). They are not insured (the spawner brings new ones instead);
/// a ship destroyed is taken away after <see cref="WreckSeconds"/>.
///
/// Any ship scene will do: what flies it is the scene's business (an AI
/// fighter patrols round where it appears). In a session only the host
/// brings ships in, and the guests see them (the scene must be one of the
/// <see cref="SessionNode.Spawnables"/>); on a guest this does nothing.
/// Place it under the floating origin.
/// </summary>
public partial class ShipSpawner : Node3D
{
    [Export] public PackedScene Scene;
    [Export] public int Count = 2;
    [Export] public double FirstSeconds = 5;
    [Export] public double IntervalSeconds = 20;
    [Export] public float Radius = 500;
    [Export] public double WreckSeconds = 30;
    [Export] public int Seed = 1;

    /// <summary>The ships brought in that are still here (alive or wrecked).</summary>
    public IReadOnlyList<Node3D> Ships => _ships.Select(s => s.Ship).Where(s => IsInstanceValid(s) && !s.IsQueuedForDeletion()).ToList();

    private readonly List<(Node3D Ship, IRadarTarget Signature)> _ships = new();
    private readonly Dictionary<Node3D, double> _wrecked = new();
    private RandomNumberGenerator _random;
    private double _wait;
    private int _made;

    public override void _Ready()
    {
        _random = new RandomNumberGenerator { Seed = (ulong)Seed };
        _wait = FirstSeconds;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Scene == null || SessionNode.Instance?.Session is { IsHost: false })
            return;
        _ships.RemoveAll(s => !IsInstanceValid(s.Ship) || !s.Ship.IsInsideTree());
        foreach ((Node3D ship, IRadarTarget signature) in _ships)
        {
            if (signature?.Alive != false)
                continue;
            _wrecked[ship] = _wrecked.GetValueOrDefault(ship) + delta;
            if (_wrecked[ship] >= WreckSeconds)
                ship.QueueFree();
        }
        foreach (Node3D gone in _wrecked.Keys.Where(s => !IsInstanceValid(s) || s.IsQueuedForDeletion()).ToList())
            _wrecked.Remove(gone);

        _wait -= delta;
        if (_wait > 0 || _ships.Count(s => s.Signature?.Alive != false && !s.Ship.IsQueuedForDeletion()) >= Count)
            return;
        _wait = IntervalSeconds;
        Bring();
    }

    private void Bring()
    {
        if (Scene.Instantiate() is not Node3D ship)
            return;
        Vector3 offset = new Vector3(_random.RandfRange(-1, 1), _random.RandfRange(-1, 1), _random.RandfRange(-1, 1)).LimitLength(1) * Radius;
        var facing = new Basis(new Quaternion(_random.RandfRange(-1, 1), _random.RandfRange(-1, 1), _random.RandfRange(-1, 1), _random.RandfRange(-1, 1)).Normalized());
        ship.Name = $"{Name}{++_made}";
        ship.Transform = new Transform3D(facing, Position + offset);     // beside this node, in the same parent
        var parts = ship.FindChildren("*", "", true, false).Prepend(ship).ToList();
        foreach (NetBody body in parts.OfType<NetBody>())
            body.Spawned = true;
        foreach (ShipHull hull in parts.OfType<ShipHull>())
            hull.Insured = false;
        GetParent().AddChild(ship);
        _ships.Add((ship, parts.OfType<IRadarTarget>().FirstOrDefault()));
    }
}
