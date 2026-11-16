using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Scouts and liveries:
///   - the two scouts are what their specs say: faster than the fighter, the S4 faster in cruise, quieter and with a bigger
///     tank than the S3, both under what the inertial unit and the wire carry (4096 m/s);
///   - an owner runs as many scouts as a round of its home's trading partners needs, no more than its fleet line says;
///   - a scout goes to the partner its owner knows least freshly, on to others while what it carries stays good, and home;
///     the owner learns it all only when it lands there, and nothing it brings is older than news stays good;
///   - with everything known freshly it stays home;
///   - owners with scouts know their partners' markets more freshly than without;
///   - an owner's ships wear its livery: its paint in place of Synty's atlas and of other paints, not of glass; people with
///     no company wear a free one, the same on every machine; a spawn tells the guests which.
///
/// Run: Godot --headless --path . res://tests/scout_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class ScoutTest : Node
{
    private const float Dt = 1f / 60;
    private int _failures;

    public override void _Ready()
    {
        Specs();
        Records();
        Freshness();
        Liveries();
        Spawn();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Specs()
    {
        ShipModel s3 = GD.Load<ShipModel>("res://assets/ship/models/scout_s3.tres"), s4 = GD.Load<ShipModel>("res://assets/ship/models/scout_s4.tres");
        ShipSpec fighter = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_fighter.tres");
        float thrust = s3.Engine.RatedThrust;
        float top3 = s3.Hull.TopSpeed(thrust), top4 = s4.Hull.TopSpeed(thrust), topFighter = fighter.TopSpeed(thrust);
        Check($"both outrun the fighter on the engine alone (S3 {top3:0}, S4 {top4:0}, fighter {topFighter:0} m/s)", top3 > topFighter * 1.2f && top4 > top3);
        float Cruise(ShipModel m) => m.Hull.TopSpeed(m.Generator.Push, m.Hull.VeilDrag / m.Generator.Thinning);
        float c3 = Cruise(s3), c4 = Cruise(s4);
        Check($"the S4 is faster in cruise (S3 {c3:0}, S4 {c4:0} m/s), both under 4096", c4 > c3 && c4 < 4096 && c3 < 4096);
        Check($"and spools its generator sooner ({s3.Generator.SpoolSeconds} s, {s4.Generator.SpoolSeconds} s)", s4.Generator.SpoolSeconds < s3.Generator.SpoolSeconds);
        Check($"and goes further on a tank ({s3.Hull.FuelTank} t, {s4.Hull.FuelTank} t)", s4.Hull.FuelTank > s3.Hull.FuelTank);

        Node scene3 = s3.Scene.Instantiate(), scene4 = s4.Scene.Instantiate();
        var sig3 = scene3.GetNode<ShipSignature>("Signature");
        var sig4 = scene4.GetNode<ShipSignature>("Signature");
        Check($"the S4 echoes less ({sig3.RadarCrossSection} m², {sig4.RadarCrossSection} m²) and runs cooler", sig4.RadarCrossSection < sig3.RadarCrossSection && sig4.MotorHeat < sig3.MotorHeat && sig4.HullHeat < sig3.HullHeat);
        Check("and leaves a fainter wake in cruise", s4.Generator.WakeEcho < s3.Generator.WakeEcho);
        foreach ((Node scene, ShipModel model) in new[] { (scene3, s3), (scene4, s4) })
        {
            var hardpoints = Fittings.Of(model.Hull, null).Keys.ToHashSet();
            Check($"{model.Name}: two lasers on its nose hardpoints, no missiles", hardpoints.SetEquals(new[] { "NoseLeft", "NoseRight" }) && model.Hull.MissileRack == 0 && scene.FindChildren("*", "LauncherDevice", true, false).Count == 0);
            Check($"{model.Name}: hitboxes, a cruise generator and a radio", scene.GetNode("Hitboxes").GetChildCount() > 10 && scene.GetNodeOrNull("Systems/Cruise") != null && scene.GetNodeOrNull("Sensors/Radio") != null);
            scene.Free();
        }
    }

    private (UniverseState, StationBook, Func<string, IAccount>) World()
    {
        StationBook book = StationBook.Read(GD.Load<SectorMap>("res://assets/sectors/map.tres"));
        var state = new UniverseState();
        var owners = new Dictionary<string, OrganisationRecord>();
        IAccount Account(string id) => CargoShipsTest.Account(state, owners, id);
        foreach (StationBook.Gate gate in book.Gates)
            Account(gate.Owner);
        CargoShips.Muster(state, book, Account);
        return (state, book, Account);
    }

    private void Records()
    {
        (UniverseState state, StationBook book, Func<string, IAccount> Account) = World();
        foreach (StationBook.Station station in book.Stations)
            foreach (FleetLine line in (station.Fleet ?? []).Where(l => l?.Model?.Scout == true))
            {
                double round = CargoShips.Round(station, station.Owner, line.Model, state, book, Account);
                int wanted = (int)Math.Ceiling(round / (line.Model.NewsHours * 3600 / 2));
                int have = state.CargoShips.Values.Count(s => s.Home == station.Id && s.Model == line.Model.ResourcePath);
                Check($"{station.Id}: a round of {CargoShips.Partners(station, station.Owner, line.Model, state, book, Account).Count()} partners takes {round / 3600:0.0} h: {have} scouts ({wanted} wanted, at most {line.Count})",
                    round > 0 && have == Math.Min(wanted, line.Count) && have > 0);
            }

        StationBook.Station alpha = book.Find("ALPHA");
        CargoShipRecord scout = state.CargoShips.Values.First(s => s.Home == alpha.Id && ResourceLoader.Load<ShipModel>(s.Model).Scout);
        ShipModel model = ResourceLoader.Load<ShipModel>(scout.Model);
        IAccount miltec = Account(alpha.Owner);
        var partners = CargoShips.Partners(alpha, alpha.Owner, model, state, book, Account).Select(s => s.Id).ToHashSet();
        Tick(state, book, Account, 10);
        Check($"knowing nothing, the scout sets out for a partner ({scout.Going}: {string.Join(", ", partners)})", scout.Phase == CargoShipRecord.Flying && partners.Contains(scout.Going));
        var visited = new List<string>();
        double setOut = state.Clock;
        string last = "";
        for (double t = 0; t < 12 * 3600 && !(scout.At == alpha.Id && scout.Phase == CargoShipRecord.Parked && visited.Count > 0); t += 5)
        {
            Tick(state, book, Account, 5);
            if (scout.Phase == CargoShipRecord.Parked && scout.At != alpha.Id && scout.At != last)
            {
                visited.Add(last = scout.At);
                if (visited.Count == 1)
                    Check("on the first partner its owner still knows nothing of it", !miltec.Known.ContainsKey(scout.At) && scout.News.ContainsKey(scout.At));
            }
        }
        Check($"it looked at {visited.Count} partners ({string.Join(", ", visited)}) and came home in {(state.Clock - setOut) / 3600:0.0} h", scout.At == alpha.Id && visited.Count > 0 && visited.All(partners.Contains));
        Check("home, the owner knows every market it saw", visited.All(miltec.Known.ContainsKey) && scout.News.Count == 0);
        if (visited.Count == 0)
            return;
        double oldest = visited.Min(s => miltec.Known[s].AsOf);
        Check($"none of it older than news stays good ({(state.Clock - oldest) / 3600:0.0} h of {model.NewsHours} h)", state.Clock - oldest <= model.NewsHours * 3600);

        foreach (string id in partners)
            miltec.Known[id] = new StationNews { AsOf = state.Clock };
        scout.NextLook = 0;
        Tick(state, book, Account, 10);
        Check("with every partner known freshly it stays home", scout.Phase == CargoShipRecord.Parked && scout.At == alpha.Id);
    }

    // The same day twice, with the scouts at work and with them grounded: what the owners know of their partners, on average
    private void Freshness()
    {
        double Age(bool scouts)
        {
            (UniverseState state, StationBook book, Func<string, IAccount> Account) = World();
            if (!scouts)
                foreach (CargoShipRecord ship in state.CargoShips.Values.Where(s => ResourceLoader.Load<ShipModel>(s.Model).Scout))
                    ship.NextLook = 1e12;
            double sum = 0;
            int samples = 0;
            for (double t = 0; t < 24 * 3600; t += 10)
            {
                Tick(state, book, Account, 10);
                if (t < 6 * 3600 || (int)t % 600 != 0)
                    continue;
                foreach (StationBook.Station home in book.Stations.Where(s => s.Fleet?.Any(l => l?.Model?.Scout == true) == true))
                {
                    string id = CargoShips.OwnerOf(state, home);
                    IAccount owner = Account(id);
                    ShipModel model = home.Fleet.First(l => l?.Model?.Scout == true).Model;
                    foreach (StationBook.Station partner in CargoShips.Partners(home, id, model, state, book, Account))
                    {
                        sum += Math.Min(24 * 3600, state.Clock - (owner.Known.GetValueOrDefault(partner.Id)?.AsOf ?? double.MinValue / 2));
                        samples++;
                    }
                }
            }
            return sum / Math.Max(1, samples) / 3600;
        }
        double with = Age(true), without = Age(false);
        Check($"with scouts the owners' news of their partners is fresher ({with:0.0} h old on average, {without:0.0} h without)", with < without * 0.75);
    }

    private void Liveries()
    {
        Material miltec = GD.Load<Material>("res://assets/materials/paint_miltec.tres");
        Check("an organisation's ships wear its paint", LiveryCatalog.Of("miltec") == miltec && LiveryCatalog.IndexOf(miltec) > 0 && LiveryCatalog.At(LiveryCatalog.IndexOf(miltec)) == miltec);
        Material free = LiveryCatalog.Of("person-7");
        Check("someone with no company wears a free paint, always the same", free != null && LiveryCatalog.Loaded.Free.Contains(free) && LiveryCatalog.Of("person-7") == free);
        Check("not everyone the same one", Enumerable.Range(0, 20).Select(i => LiveryCatalog.Pick($"person-{i}")).Distinct().Count() > 2);

        Node3D ship = GD.Load<PackedScene>("res://scenes/ships/scout_s4.tscn").Instantiate<Node3D>();
        LiveryCatalog.Wear(ship, miltec);
        Check("wearing it, its network body says which", ship.GetNode<NetBody>("NetBody").Look == LiveryCatalog.IndexOf(miltec));
        var paint = ship.GetNode<ModelPaint>("Exterior/Model/Paint");
        Material atlas = GD.Load<Material>("res://assets/materials/synty_atlas.tres");
        Material glass = GD.Load<Material>("res://assets/materials/synty_cockpit_glass.tres");
        Material freighter = GD.Load<Material>("res://assets/materials/paint_freighter.tres");
        Check("its paint goes in place of Synty's atlas and of another paint", PaintTheme.Over(paint, atlas) == miltec && PaintTheme.Over(paint, freighter) == miltec);
        Check("not of glass", PaintTheme.Over(paint, glass) == glass);
        ship.Free();
        Node3D freighterShip = GD.Load<PackedScene>("res://scenes/ships/ai_freighter.tscn").Instantiate<Node3D>();
        LiveryCatalog.Wear(freighterShip, miltec);
        var part = freighterShip.FindChildren("*", "", true, false).OfType<ModelPaint>().First();
        Check("a freighter's own theme gives way to its owner's paint", PaintTheme.Over(part, atlas) == miltec);
        freighterShip.Free();
    }

    private sealed class CodeShip : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes => [];
        public ShipSnapshot Snapshot(uint tick) => new() { Tick = tick, Orientation = Quaternion.Identity };
        public void TakeHit(HitEvent hit) { }
    }

    private void Spawn()
    {
        var net = new SimulatedNetwork { Latency = 0.05 };
        var host = new Session(Session.HostPeer, net.Join(Session.HostPeer)) { TickSeconds = Dt };
        var guest = new Session(1, net.Join(1)) { TickSeconds = Dt };
        byte seen = 0;
        guest.SpawnDynamic = (id, scene, look) =>
        {
            seen = look;
            return new CodeShip { NetId = id, Track = new RemoteTrack { TickSeconds = Dt } };
        };
        bool spawned = false;
        for (uint tick = 1; tick < 600 && seen == 0; tick++)
        {
            net.Now = tick * Dt;
            host.Tick(tick);
            guest.Tick(tick);
            if (!spawned && guest.Synced)
            {
                host.Spawn(new CodeShip { NetId = host.NewDynamicId(), IsLocal = true }, 3, 0, 7);
                spawned = true;
            }
        }
        Check($"a spawn tells the guest which livery ({seen})", seen == 7);
    }

    private static void Tick(UniverseState state, StationBook book, Func<string, IAccount> account, double seconds)
    {
        state.Clock += seconds;
        CargoShips.Tick(state, book, account);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
