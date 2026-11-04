using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// A hull built of parts:
///
///   - what parts come to: their masses added, the inertia of their boxes where they stand;
///   - the freighter's hull holds what its parts come to (run tools/ship_mass/ship_mass_tool.tscn if not);
///   - its parts' boxes are its hitboxes, and what hits one reaches the hull;
///   - it fits a small landing pad;
///   - it takes off and flies, its AI at the controls, and is slower than the fighter;
///   - its engines' flames show the same push, always.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/ship_parts_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class ShipPartsTest : Node3D
{
    private int _failures;

    public override async void _Ready()
    {
        var exterior = new Node3D();
        var part = new ShipPart { Mass = 1200, Position = new Vector3(0, 0, 10) };
        part.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1, 2, 3) } });
        exterior.AddChild(part);
        ShipPart.Sum sum = ShipPart.Totals(exterior);
        var inertia = new Vector3(1200f / 12 * (4 + 9) + 1200 * 100, 1200f / 12 * (1 + 9) + 1200 * 100, 1200f / 12 * (1 + 4));
        Check($"one part: its mass and its inertia about the origin ({sum.Inertia})", sum.Mass == 1200 && sum.Inertia.IsEqualApprox(inertia) && sum.Centre.IsEqualApprox(new Vector3(0, 0, 10)));
        exterior.Free();

        var hull = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_freighter.tres");
        var built = hull.Exterior.Instantiate<Node3D>();
        ShipPart.Sum parts = ShipPart.Totals(built);
        built.Free();
        Check($"the hull holds what its {parts.Parts} parts come to ({hull.Mass / 1000:0.0} t)", hull.Mass == parts.Mass && hull.Inertia.IsEqualApprox(parts.Inertia));

        var ship = GD.Load<PackedScene>("res://scenes/ships/ai_freighter.tscn").Instantiate<ShipBody>();
        ship.Position = new Vector3(0, 0, 0);
        AddChild(ship);
        await Seconds(0.1);

        Vector3 size = ship.Bounds.Size;
        Check($"its hitboxes are its parts' ({size.X:0.0} x {size.Y:0.0} x {size.Z:0.0} m)", size.X > 15 && size.Z > 15);
        var pad = GD.Load<PackedScene>("res://scenes/stations/props/landing_pad_small.tscn").Instantiate<LandingPad>();
        pad.Position = new Vector3(0, -5000, 0);
        AddChild(pad);
        Check("it fits a small pad", pad.Fits(ship));

        ShipHull shipHull = ship.Find<ShipHull>();
        float before = shipHull.Left;
        ship.GetNode<HitArea>("Exterior/Engine/Hit").Hit(400);
        Check($"a hit on a part reaches the hull ({before:0} to {shipHull.Left:0})", shipHull.Left < before);

        float top = hull.TopSpeed(1_000_000f), fighter = GD.Load<ShipSpec>(ShipRecord.StarterHull).TopSpeed(1_000_000f);
        Check($"its top speed is the plate's: {top:0} m/s, the fighter's {fighter:0}", top < fighter);

        float fastest = 0;
        for (int i = 0; i < 90 * Engine.PhysicsTicksPerSecond; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            fastest = Mathf.Max(fastest, ship.LinearVelocity.Length());
        }
        Check($"it flies ({fastest:0} m/s at most)", fastest > 20 && fastest < top * 1.1f);

        EnginePlume[] plumes = ship.FindChildren("*", "", true, false).OfType<EnginePlume>().ToArray();
        float least = 1, most = 0;
        for (int i = 0; i < 5 * Engine.PhysicsTicksPerSecond; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Alike(plumes[0].Shown == plumes[1].Shown);
            least = Mathf.Min(least, plumes[0].Shown);
            most = Mathf.Max(most, plumes[0].Shown);
        }
        Check($"its engines burn alike, all the time ({plumes.Length} flames, {_unlike} frames apart; push shown {least:0.00} to {most:0.00})", plumes.Length == 2 && _unlike == 0);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private int _unlike;

    private void Alike(bool same)
    {
        if (!same)
            _unlike++;
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
