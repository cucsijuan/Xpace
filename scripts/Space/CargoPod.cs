using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.World;

namespace Xpace.Space;

/// <summary>
/// Cargo out in space: what a ship threw out or left when it blew up, one pod for each good, drifting. Anyone can take
/// it: a pilot flying slowly past scoops what fits their hold; a pirate goes for it (<see cref="CargoView"/>). The pod
/// knows whose it was: taken by anyone else, or stolen already, it is stolen goods (<see cref="StolenBy"/>). It belongs
/// to the universe's owner: other machines see it as any ship the game brings in. Lost cargo someone is paid to recover
/// is consigned to its taker instead (<see cref="Contracts.Scooped"/>). A pod may hold a thing instead of cargo
/// (<see cref="Item"/>: a flight recorder's diskette), taken by the first pilot with room on them for it.
/// </summary>
public partial class CargoPod : Node3D
{
    public const string Group = "cargo_pods";
    private const string Scene = "res://scenes/space/cargo_pod.tscn";

    [Export] public string Good = "";
    [Export] public int Tonnes;
    /// <summary>Whose it was, as an actor (<see cref="Universe.ActorOf"/>); empty: nobody's known.</summary>
    [Export] public string Owner = "";
    /// <summary>It was stolen goods already in the hold it came out of.</summary>
    [Export] public bool Stolen;
    /// <summary>How long it lasts before it is lost among everything else out there, seconds (0: until it is taken).</summary>
    [Export] public double LifeSeconds = 900;
    /// <summary>How near a ship must come to take it (m), and how slowly (m/s) against it.</summary>
    [Export] public float ScoopMeters = 40f;
    [Export] public float ScoopSpeed = 60f;
    /// <summary>How long it drifts clear of who threw it out before it can be taken, seconds.</summary>
    [Export] public double ClearSeconds = 5;
    [Export] public double LookEverySeconds = 0.25;

    public Vector3 Velocity;
    /// <summary>A thing in it instead of cargo (null: none).</summary>
    public ItemRecord Item;
    /// <summary>The contract whose wreck it lies at (<see cref="StorySites"/>; empty: none).</summary>
    public string Site = "";


    private double _age, _wait;

    public override void _Ready()
    {
        AddToGroup(Group);
        if (FindChild("Model", false, false) is Node3D model)
            model.Rotation = new Vector3(GetInstanceId() % 7, GetInstanceId() % 5, GetInstanceId() % 3);
    }

    /// <summary>
    /// Throws goods out of a ship, by the universe's owner: a pod for each, spreading a little. <paramref name="owner"/>
    /// is whose they are; <paramref name="stolen"/>, what of them was stolen already (a pod of its own for each good).
    /// </summary>
    public static void Spill(Node3D ship, Vector3 velocity, IEnumerable<KeyValuePair<string, int>> goods, string owner, IReadOnlyDictionary<string, int> stolen = null)
    {
        if (FloatingOrigin.Of(ship) is not FloatingOrigin root)
            return;
        var pods = new List<(string Good, int Tonnes, bool Stolen)>();
        foreach ((string good, int tonnes) in goods.Where(g => g.Value > 0))
        {
            int hot = Math.Min(tonnes, stolen?.GetValueOrDefault(good) ?? 0);
            if (tonnes > hot)
                pods.Add((good, tonnes - hot, false));
            if (hot > 0)
                pods.Add((good, hot, true));
        }
        int n = 0;
        foreach ((string good, int tonnes, bool hot) in pods)
        {
            var pod = Xpace.World.ResourceCache.Load<PackedScene>(Scene).Instantiate<CargoPod>();
            float angle = n++ * 2.4f;
            var out_ = new Vector3(MathF.Cos(angle), 0.3f, MathF.Sin(angle)).Normalized();
            pod.Good = good;
            pod.Tonnes = tonnes;
            pod.Owner = owner ?? "";
            pod.Stolen = hot;
            pod.Position = ship.Position + out_ * 15;
            pod.Velocity = velocity + out_ * 15;
            foreach (NetBody body in pod.FindChildren("*", "", true, false).OfType<NetBody>())
                body.Spawned = true;
            root.AddChild(pod);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (NetBody.IsPuppet(this))
            return;
        Position += Velocity * (float)delta;
        _age += delta;
        if (LifeSeconds > 0 && _age >= LifeSeconds || Tonnes <= 0 && Item == null)
        {
            QueueFree();
            return;
        }
        _wait -= delta;
        if (_wait > 0 || !Clear || Universe.Current is not Universe { IsGuest: false } universe)
            return;
        _wait = LookEverySeconds;
        foreach (Blip blip in RadarPicture.Of(this).Near(GlobalPosition, ScoopMeters))
            if (blip.Velocity.DistanceTo(Velocity) <= ScoopSpeed && Combat.ShipOf(blip.Target as Node) is Node3D ship && universe.PilotOf(ship) is PilotRecord pilot)
            {
                if (Item != null)
                {
                    if (pilot.Items.Count >= PilotRecord.InventorySlots)
                        continue;
                    pilot.Items.Add(System.Text.Json.JsonSerializer.Deserialize<ItemRecord>(System.Text.Json.JsonSerializer.Serialize(Item)));
                    Item = null;
                    universe.Touch(pilot);
                    QueueFree();
                    return;
                }
                int taken = Take(universe.Room(pilot));
                if (taken <= 0)
                    continue;
                int consigned = Stolen ? 0 : Contracts.Scooped(universe.State, pilot.Id, Owner, Good, taken);
                if (consigned > 0)
                    pilot.Ship.Consign(Good, consigned);
                if (taken > consigned && StolenBy(pilot.Id))
                    pilot.Ship.Steal(Good, taken - consigned);
                else if (taken > consigned)
                    pilot.Ship.Load(Good, taken - consigned);
                universe.Touch(pilot);
            }
    }

    /// <summary>It has drifted clear of who threw it out: anyone can take it.</summary>
    public bool Clear => _age >= ClearSeconds;

    /// <summary>Taken by <paramref name="actor"/>, it is stolen goods: it was stolen already, or it was not theirs.</summary>
    public bool StolenBy(string actor) => Stolen || string.IsNullOrEmpty(Owner) || Owner != actor;

    /// <summary>A ship takes up to that many tonnes out of it; what it took.</summary>
    public int Take(int most)
    {
        int taken = Math.Min(Tonnes, most);
        Tonnes -= taken;
        return taken;
    }
}
