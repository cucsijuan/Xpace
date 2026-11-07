using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// No world position goes stale when the origin moves. Every field in the game that can hold a position must be one of:
/// a <see cref="Spot"/> (it isn't looked at here), a speed or direction by its name, or on one of the lists below, each
/// with its reason. A new field fails here until someone decides: if it keeps a world position past the step, it is a
/// spot. A field on <see cref="Shifted"/> needs its class to be an <see cref="IOriginShift"/>. Then spots, the ship's
/// way back and a route are checked across real shifts.
///
/// Run: Godot --headless --path . res://tests/places_test.tscn
/// </summary>
public partial class PlacesTest : Node
{
    private int _failures;

    // Not places at all, by name: speeds, turns, directions, sizes
    private static readonly Regex Relative = new("Velocity|Spin|Normal|Heading|Facing|Aim|Direction|HalfSize|Inertia|Look$|Rays|_jink|_breakSide|_stick|_applied|_uplink|_toTarget|Bounds|Offset", RegexOptions.IgnoreCase);

    // In a node's or ship's own frame: they move with it
    private static readonly HashSet<string> Local = new()
    {
        "Cockpit.CockpitLever._rest", "Cockpit.Pilot._headPose", "Cockpit.Pilot._framed", "Controls.InteractiveControl.PartRest",
        "Cockpit.CruiseDust._at", "Menu.MenuDust._at", "Net.Hitbox.Centre", "Ship.ShipPart+Sum.Centre", "Space.BoneMount._offset",
        "Space.HangarMark._rest", "Space.JumpGate._was", "Space.LandingPad._from", "Space.LandingPad+Guidance.Offset", "Space.MeshSlide._shut",
        "Space.RockField+Rock.Offset", "Ship.FighterAi._plumes", "Space.Solid.A", "Space.Solid.B", "Space.Solid.C", "Space.LandingPad.s_bounds", "Ship.ShipContact+Contact.Normal",
    };

    // Coordinates of a sector, which never shift: a record's, a map's
    private static readonly Regex SectorCoordinates = new(@"^World\.(StationBook|Voyage)");

    // Worked out again each step or frame, and read within it (the origin moves at the end of the step, after everyone)
    private static readonly HashSet<string> PerStep = new()
    {
        "Net.SessionNode+Speaker.RadioPosition", "Net.WalkerState.Position", "OnFoot.Walker.AimPoint", "Ship.JammerDevice._at", "Ship.RadarDevice._centroid",
        "Ship.RadarDevice._jamAt", "Ship.ShipContact+Triangle.U", "Ship.ShipContact+Triangle.V", "Ship.ShipContact+Triangle.W", "Ship.ShipContact+Triangle.H",
        "Ship.ShipContact+Triangle.A", "Ship.ShipContact+Triangle.B", "Ship.ShipContact+Triangle.C", "Ship.ShipState.Position", "Space.Blip.Position",
        "Space.FalseEcho.GlobalPosition", "Space.JamLine.To", "Space.JamSource.Position", "Space.Ping.Position", "Space.RadarEmitter._at", "Space.RockSphere.Centre",
        "Space.Combat.Fired", "Space.Combat.Destroyed", "Space.Spatial+Layer.Where", "Space.SpatialGrid`1._at",
    };

    // Too many, or drawn every frame: their classes are told of a shift
    private static readonly HashSet<string> Shifted = new() { "Space.CruiseWake._at", "Space.MissilePlume._trail", "Space.Projectiles+Round.Position" };

    private static bool Holds(Type t, int depth = 0)
    {
        if (t == typeof(Vector3) || t == typeof(Transform3D) || t == typeof(Aabb))
            return true;
        if (depth > 4 || t.IsPrimitive || t == typeof(string) || t.IsEnum)
            return false;
        if (t.IsArray)
            return Holds(t.GetElementType(), depth + 1);
        return t.IsGenericType && t.GetGenericArguments().Any(a => Holds(a, depth + 1));
    }

    public override async void _Ready()
    {
        Fields();
        await Shifts();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Fields()
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        int seen = 0;
        foreach (Type t in typeof(PlacesTest).Assembly.GetTypes().Where(t => t.FullName.StartsWith("Xpace.") && !t.FullName.StartsWith("Xpace.Tests") && !t.Name.Contains('<')))
            foreach (FieldInfo f in t.GetFields(All).Where(f => Holds(f.FieldType) && f.GetCustomAttribute<ExportAttribute>() == null))
            {
                seen++;
                string name = f.Name.Replace("k__BackingField", "").Trim('<', '>');
                string full = $"{t.FullName.Replace("Xpace.", "")}.{name}";
                if (Shifted.Contains(full))
                {
                    Type owner = t.DeclaringType ?? t;
                    if (!typeof(IOriginShift).IsAssignableFrom(owner))
                        Check($"{full} is shifted, but {owner.Name} isn't told of shifts", false);
                }
                else if (!Relative.IsMatch(name) && !Local.Contains(full) && !PerStep.Contains(full) && !SectorCoordinates.IsMatch(full))
                    Check($"{full} ({f.FieldType.Name}) may keep a world position: use a Spot, or say why not in PlacesTest", false);
            }
        Check($"every field that can hold a position has been decided on ({seen})", _failures == 0);
    }

    private async Task Shifts()
    {
        var root = new FloatingOrigin();
        AddChild(root);
        var thing = new Node3D { Position = new Vector3(100, 0, 0) };
        root.AddChild(thing);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        Spot spot = Spot.At(thing, new Vector3(500, 20, -30));
        Check("a spot is where it was put", spot.World.IsEqualApprox(new Vector3(500, 20, -30)));
        root.Shift(new Vector3(1000, 0, 0));
        Check($"and stays there when the world moves ({spot.World})", spot.World.IsEqualApprox(new Vector3(-500, 20, -30)));
        Check("as the things in it do", (spot.World - thing.Position).IsEqualApprox(new Vector3(500, 20, -30) - new Vector3(100, 0, 0)));
        Check("a spot moved goes that far", spot.Moved(Vector3.Up).World.IsEqualApprox(new Vector3(-500, 21, -30)));
        Check("its distance is taken where the world is now", Mathf.IsEqualApprox(spot.DistanceTo(new Vector3(-500, 20, 0)), 30));
        for (int i = 0; i < 2000; i++)
            root.Shift(new Vector3(1000, 0, 0));
        Check($"two thousand shifts later nothing has crept ({spot.World.X + 2_000_500:0.000} m)", Mathf.Abs(spot.World.X + 2_000_500) < 0.5f);
        root.Shift(new Vector3(-2_000_000, 0, 0));

        var spots = new Spots(thing);
        spots.Enqueue(new Vector3(10, 0, 0));
        root.Shift(new Vector3(10, 0, 0));
        Check("a route's points stay put too", spots.Peek().IsEqualApprox(Vector3.Zero));

        var ship = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
        ship.GetNode("Pilot").ProcessMode = ProcessModeEnum.Disabled;
        foreach (Xpace.Computer.ShipComputer c in ship.FindChildren("*", "", true, false).OfType<Xpace.Computer.ShipComputer>())
            c.PowerOnAtStart = false;
        root.AddChild(ship);
        ShipHull hull = ship.Find<ShipHull>();
        hull.Spawn = new Transform3D(Basis.Identity, new Vector3(30, 0, 0));
        root.Shift(new Vector3(30, 0, 0));
        Check("the place a ship comes back to stays put", hull.Spawn.Origin.IsEqualApprox(Vector3.Zero));
        root.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
