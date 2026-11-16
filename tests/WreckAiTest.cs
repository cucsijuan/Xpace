using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// An AI fighter and a dead hull:
///   - a wreck 3 km ahead is an echo: it goes to see it, and locks it;
///   - near, it sees it lying still and cold: it lets the lock go and leaves it, and never spent a missile on it;
///   - a hull at rest but alive (its idle heat) is no wreck to it: it attacks it.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/wreck_ai_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class WreckAiTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        var world = new Node3D { Name = "World" };
        AddChild(world);
        world.AddChild(new Projectiles());
        var wreck = GD.Load<PackedScene>("res://scenes/space/freighter_wreck.tscn").Instantiate<Node3D>();
        wreck.Position = new Vector3(0, 0, -3000);
        world.AddChild(wreck);
        var echo = wreck.GetNode<WreckEcho>("Echo");
        var ai = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
        world.AddChild(ai);
        var pilot = ai.GetNode<FighterAi>("Pilot");
        pilot.RespectsZones = false;
        var radar = ai.Find<RadarDevice>();
        var launcher = ai.Find<LauncherDevice>();
        int missiles = launcher.MissilesLeft, seekers = launcher.HeatSeekersLeft;
        Check("its computer is up", await Until(() => ai.Computer.ScreenText().Contains("Threat warning resident"), 60));

        bool locked = false;
        Check("it goes to see the echo and locks it", await Until(() => locked = ReferenceEquals(radar.Locked, echo), 90));
        float closest = float.MaxValue;
        bool left = await Until(() =>
        {
            closest = Math.Min(closest, ai.GlobalPosition.DistanceTo(wreck.GlobalPosition));
            return locked && radar.Locked == null && closest < pilot.SightRange;
        }, 90);
        Check($"near ({closest:0} m) it sees a dead hull and lets it go ({pilot.Doing})", left);
        await Seconds(20);
        Check($"and leaves it be ({pilot.Doing})", !ReferenceEquals(radar.Locked, echo) && pilot.Doing != "intercepting");
        Check("no missile spent on it", launcher.MissilesLeft == missiles && launcher.HeatSeekersLeft == seekers);

        // A hull at rest, alive: its idle heat shows it is no wreck
        var still = new Drone { Name = "Still", Straight = true, Evades = false, Defends = false, Speed = 0, Health = 100_000, Position = ai.GlobalPosition + ai.GlobalBasis * new Vector3(0, 0, -1500) };
        world.AddChild(still);
        bool attacked = await Until(() => pilot.Target != null && ReferenceEquals(radar.Locked, still) && pilot.Doing != "intercepting", 60);
        Check($"a live hull at rest is attacked ({pilot.Doing})", attacked);
        Finish();
    }

    private async Task<bool> Until(Func<bool> done, float seconds)
    {
        for (int i = 0; i < seconds * Engine.PhysicsTicksPerSecond; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return done();
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Finish()
    {
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
