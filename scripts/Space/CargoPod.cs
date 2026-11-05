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
/// it: a pilot flying slowly past scoops what fits their hold; a pirate goes for it (<see cref="CargoView"/>). Nothing
/// says whose it was. It belongs to the universe's owner: other machines see it as any ship the game brings in.
/// </summary>
public partial class CargoPod : Node3D
{
    public const string Group = "cargo_pods";
    private const string Scene = "res://scenes/space/cargo_pod.tscn";

    [Export] public string Good = "";
    [Export] public int Tonnes;
    /// <summary>How long it lasts before it is lost among everything else out there, seconds.</summary>
    [Export] public double LifeSeconds = 900;
    /// <summary>How near a ship must come to take it (m), and how slowly (m/s) against it.</summary>
    [Export] public float ScoopMeters = 40f;
    [Export] public float ScoopSpeed = 60f;
    /// <summary>How long it drifts clear of who threw it out before it can be taken, seconds.</summary>
    [Export] public double ClearSeconds = 5;
    [Export] public double LookEverySeconds = 0.25;

    public Vector3 Velocity;

    private double _age, _wait;

    public override void _Ready()
    {
        AddToGroup(Group);
        if (FindChild("Model", false, false) is Node3D model)
            model.Rotation = new Vector3(GetInstanceId() % 7, GetInstanceId() % 5, GetInstanceId() % 3);
    }

    /// <summary>Throws goods out of a ship, by the universe's owner: a pod for each, spreading a little.</summary>
    public static void Spill(Node3D ship, Vector3 velocity, IEnumerable<KeyValuePair<string, int>> goods)
    {
        if (FloatingOrigin.Of(ship) is not FloatingOrigin root)
            return;
        int n = 0;
        foreach ((string good, int tonnes) in goods.Where(g => g.Value > 0))
        {
            var pod = GD.Load<PackedScene>(Scene).Instantiate<CargoPod>();
            float angle = n++ * 2.4f;
            var out_ = new Vector3(MathF.Cos(angle), 0.3f, MathF.Sin(angle)).Normalized();
            pod.Good = good;
            pod.Tonnes = tonnes;
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
        if (_age >= LifeSeconds || Tonnes <= 0)
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
                int taken = Math.Min(Tonnes, universe.Room(pilot));
                if (taken <= 0)
                    continue;
                pilot.Ship.Cargo[Good] = pilot.Ship.Cargo.GetValueOrDefault(Good) + taken;
                Tonnes -= taken;
                universe.Touch(pilot);
            }
    }

    /// <summary>It has drifted clear of who threw it out: anyone can take it.</summary>
    public bool Clear => _age >= ClearSeconds;

    /// <summary>A ship takes up to that many tonnes out of it; what it took.</summary>
    public int Take(int most)
    {
        int taken = Math.Min(Tonnes, most);
        Tonnes -= taken;
        return taken;
    }
}
