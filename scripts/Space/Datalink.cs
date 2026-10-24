using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A ship's datalink: the ships on the same <see cref="Channel"/> tell each
/// other where they are. That is all that makes them friends: to each other
/// they are known; anything else a radar sees is not known to be. Nothing
/// here is about kinds of ships: any ship with the link on the channel is
/// on it. The ships on it also say when they fire a missile, so a friend's
/// missile flying past is known for what it is. A dead ship's link goes
/// quiet.
/// Put it under the ship.
/// </summary>
public partial class Datalink : Node
{
    [Export] public string Channel = "";

    public Node3D Ship { get; private set; }

    /// <summary>Where the ship says it is.</summary>
    public Vector3 Position => Ship.GlobalPosition;

    public bool Alive => _signature?.Alive ?? true;

    private IRadarTarget _signature;

    public override void _Ready()
    {
        Ship = GetParent<Node3D>();
        AddToGroup("datalinks");
        _signature = Ship as IRadarTarget ?? Ship.GetChildren().OfType<IRadarTarget>().FirstOrDefault();
    }

    /// <summary>The others on this link's channel that are talking (not this ship).</summary>
    public IEnumerable<Datalink> Others() =>
        GetTree().GetNodesInGroup("datalinks").OfType<Datalink>().Where(d => d != this && d.Channel == Channel && d.Alive);

    /// <summary>Whether a ship is on this link's channel (it says so: a friend's missile is one it says it fired).</summary>
    public bool Knows(Node ship) =>
        ship != null && IsInstanceValid(ship) && Others().Any(d => d.Ship == ship) || ship == Ship;

    /// <summary>Whether something seen at <paramref name="point"/> is a ship on this link (within <paramref name="gate"/> m of where one says it is).</summary>
    public bool Friendly(Vector3 point, float gate) => Others().Any(d => d.Position.DistanceTo(point) < gate);
}
