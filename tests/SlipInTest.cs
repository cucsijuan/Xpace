using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// A free pirate slipping into a station's hangar, seen, in the real game: the same watches that see a pilot judge it.
///
///   - set off for Alpha to copy its registry with no callsign that passes, it comes in along the hull, radio off, to
///     beside a hangar's mouth, and waits there for the door: the hangar's watches (approach radar, heat camera) never
///     notice it;
///   - the door opened for someone else, it goes in onto a free pad, unseen still, and the pad has it;
///   - its crew traced at the terminal: the hangar's watch is told of a ship nobody can name, which is in the hangar:
///     hostile, as a pilot would be;
///   - another one waiting out in front of a mouth instead: the watch notices it, and it turns away and keeps away.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/slip_in_test.tscn (XPACE_SLIP_SHOT=file.png, in a window,
/// saves a picture of the pirate waiting beside the mouth).
/// Exits with 0 if every check passed.
/// </summary>
public partial class SlipInTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Universe universe = Universe.Current;
        universe.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        await Seconds(0.5);
        CargoView view = main.GetNode<CargoView>("CargoView");
        StationBook book = universe.Book;
        UniverseState state = universe.State;
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e12;                              // only the pirates do anything
        StationBook.Station alpha = book.Find("ALPHA"), quartz = book.Find("QUARTZ");
        StationRadio station = StationRadio.Called(alpha.Id);
        station.Security.PoliceCount = 0;
        station.Security.Scans = false;

        var shady = GD.Load<Personality>("res://assets/personalities/shady.tres");
        var fighter = GD.Load<ShipModel>("res://assets/ship/models/free_fighter.tres");
        PersonRecord Pirate(string id)
        {
            var person = new PersonRecord { Id = PersonRecord.OwnerPrefix + id, First = "Test", Last = id, Livelihood = "Pirate", Character = shady.ResourcePath, Credits = 50_000 };
            state.People[person.Id] = person;
            CargoShips.Enlist(person.Id, $"{person.Id}-ship", fighter, quartz, state, book);
            return person;
        }

        // Off to slip in
        PersonRecord pirate = Pirate("slip");
        CargoShipRecord record = state.CargoShips[$"{pirate.Id}-ship"];
        Check("it sets off to slip into Alpha, radio off", CargoShips.Slip(record, fighter, alpha, state, book, universe.Account) && record.SlipDock > 0 && record.Unseen);
        HangarDock dock = station.Docks.First(d => d.Number == record.SlipDock);
        // Most of the way already flown
        double total = record.Arrives - record.Left;
        record.Left = state.Clock - (total - 40);
        record.Arrives = state.Clock + 40;
        bool noticed = false, quiet = true;
        ShipBody ship = null;
        for (int i = 0; i < 60 * 70 && !(record.Phase == CargoShipRecord.Holding && ship != null); i++)
        {
            await Seconds(1.0 / 60);
            if (ship == null)
                view.Shown.TryGetValue(record.Id, out ship);
            if (ship != null)
            {
                noticed |= station.Docks.Any(d => d.Watch?.NoticedAgo(ship) != null);
                quiet &= ship.Device<RadioDevice>() is not { Powered: true };
            }
        }
        Check($"shown, it came in along the hull to beside hangar {dock.Number}'s mouth ({Off(ship, dock.GlobalPosition):0} m off it), radio off all the way",
              ship != null && record.Phase == CargoShipRecord.Holding && Off(ship, dock.GlobalPosition) < 100 && quiet);
        await Seconds(4);
        noticed |= station.Docks.Any(d => d.Watch?.NoticedAgo(ship) != null);
        Check("no watch noticed it, coming or waiting", !noticed && !dock.IsOpen && record.Phase == CargoShipRecord.Holding);
        if (OS.GetEnvironment("XPACE_SLIP_SHOT") is { Length: > 0 } shot && ship != null)
        {
            // How it waits beside the mouth, seen from out in front of the hangar and a little to its side
            var eye = new Camera3D { Far = 20000 };
            main.Root(alpha.Sector).AddChild(eye);
            Vector3 look = (ship.GlobalPosition + dock.GlobalPosition) / 2;
            eye.GlobalPosition = look + dock.GlobalBasis.Z * 70 + dock.GlobalBasis.Y * 15;
            GD.Print($"slip shot: ship at {dock.GlobalTransform.AffineInverse() * ship.GlobalPosition} in the dock's frame");
            eye.LookAt(look, dock.GlobalBasis.Y);
            eye.MakeCurrent();
            await Seconds(1);
            GetViewport().GetTexture().GetImage().SavePng(shot);
            eye.QueueFree();
        }

        // The door opens for someone else
        station.OpenFor(dock.Number, main.Ship, 120);
        Check("the door opens for someone else: in it goes", await Until(() => record.Phase == CargoShipRecord.Landing, 6));
        Check("down on a pad nobody cleared it to", await Until(() => record.Phase == CargoShipRecord.Parked && record.Stowed, 90));
        await Seconds(2);
        LandingPad pad = dock.PadNumbered(record.Pad);
        noticed |= station.Docks.Any(d => d.Watch?.NoticedAgo(ship) != null);
        Check($"unseen still, on pad {record.Pad}, as the pad reckons it", !noticed && pad != null && pad.Docks(ship) && dock.Holds(ship));

        // Traced at the terminal
        alpha.RegistryTraceChance = 1;
        record.NextLook = 0;
        Check("its crew traced at the terminal: the watch is told, and a ship nobody can name in the hangar is hostile",
              await Until(() => station.Security.StandingOf(ship) == Standing.Hostile, 5) && dock.Watch.NoticedAgo(ship) != null);

        // Waiting in plain sight
        PersonRecord bold = Pirate("bold");
        CargoShipRecord rash = state.CargoShips[$"{bold.Id}-ship"];
        StationBook.Pad mouth = alpha.Pads.First(p => p.Dock == dock.Number);
        Vector3 front = mouth.Door + (mouth.Door - mouth.Inside).Normalized() * 600;
        state.Station(quartz.Id).CargoPads.Remove(CargoShips.Key(rash.Dock, rash.Pad));
        rash.Dock = rash.Pad = 0;
        rash.Going = alpha.Id;
        rash.SlipDock = dock.Number;
        rash.Phase = CargoShipRecord.Holding;
        rash.ContactPoint = front;
        rash.Arrives = state.Clock;
        rash.AskedHold = true;
        rash.NextLook = 1e12;                                   // it would wait there for ever: the watch has to see it
        Check("one waiting out in front of a mouth, radio off: the watch notices it, and it turns away",
              await Until(() => rash.SlipDock == 0 && rash.Phase == CargoShipRecord.Flying && rash.Going != alpha.Id, 10));
        Check("and keeps away from there a while", bold.Spotted.ContainsKey(alpha.Id) && !CargoShips.Heist(alpha, bold, state, universe.Account));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static float Off(Node3D ship, Vector3 at) => ship == null ? float.MaxValue : ship.GlobalPosition.DistanceTo(at);

    private async Task<bool> Until(Func<bool> done, double seconds)
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
