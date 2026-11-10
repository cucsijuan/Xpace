using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Controls;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// False callsigns, from a station's registry to a ship found out at a hangar's door:
///
///   - a pilot whose ship is in a hangar unseen writes the signatures of a station's registry core to the hold: registry
///     data, a good the law forbids, a tonne for so many signatures; again too soon, nothing new; a pilot the station
///     cleared in who is traced at its terminal has a bounty for it (the terminal itself: registry_terminal_test);
///   - police scans fine forbidden goods;
///   - a pirate base sells false callsigns (each one new, on record as false) and SPOOF on a diskette, things the pilot
///     carries; a callsign sold back is no more;
///   - SPOOF, put in drive B: and copied onto the ship's diskette with COS, has the radio send the callsign typed in;
///   - a station clears a ship sending a false callsign as a ship of its own, a docking less left on it; it turns away
///     a callsign on nobody's record, and one with no dockings left;
///   - where security is tight, the hangar's registration reader reads the hull at the door: the callsign is no good
///     any more, the ship is hostile and its pilot has a bounty.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/forged_callsign_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class ForgedCallsignTest : Node3D
{
    private StationRadio _station;
    private StationSecurity _security;
    private StationRegistry _registry;
    private HangarDock _dock;
    private ShipBody _ship;
    private int _failures;

    public override async void _Ready()
    {
        Universe universe = Universe.Current;
        universe.Forget();
        AddChild(new Projectiles());
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        AddChild(station);
        _station = station.GetNode<StationRadio>("Radio");
        _security = station.GetNode<StationSecurity>("Security");
        _registry = station.GetNode<StationRegistry>("Registry");
        _security.Scans = false;
        _security.PoliceCount = 0;
        _dock = station.GetNode("HangarBig1").GetNode<HangarDock>("Dock");

        // The pilot's ship, its computer booting COS
        _ship = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
        _ship.GetNode("Pilot").Free();
        _ship.Position = _dock.InsidePoint;
        AddChild(_ship);
        _ship.GlobalBasis = _dock.GlobalBasis;
        universe.Fly(_ship, "rook");
        PilotRecord pilot = universe.State.Pilot("rook");
        pilot.Ship.Hull = "res://assets/ship/hulls/hull_fighter.tres";
        pilot.Ship.Empty();
        pilot.Credits = 50_000;
        var radio = _ship.Find<RadioDevice>();
        await Seconds(0.5);

        // The registry
        Check("in a hangar, not cleared: the terminal is there for it", _registry.Serve("rook", Ask(RegistryRequest.Look)).Present);
        RegistryView open = _registry.Serve("rook", Ask(RegistryRequest.Open));
        List<string> live = Registries.Core(open.Seed, open.Records, open.Known).Records.Values.ToList();
        Check($"a service session: the core has {live.Count} signatures", open.Session && live.Count == _registry.Records);
        RegistryView one = _registry.Serve("rook", Ask(RegistryRequest.Write, live.Take(1)));
        Check($"one signature is not a tonne ({one.Message})", pilot.Ship.Holds("registry_data") == 0 && one.Session);
        RegistryView copied = _registry.Serve("rook", Ask(RegistryRequest.Write, live.Concat(new[] { "XX-0000" })));
        int tonnes = pilot.Ship.Holds("registry_data");
        Check($"all of them carved: {tonnes} t of registry data in the hold ({copied.Message})",
              tonnes == _registry.MaxTonnes && copied.Written == tonnes && !copied.Session && StationRegistry.Good.Illegal);
        Check("unseen: nothing on the pilot's record", universe.State.Station(_station.Id).CrimesOf("rook").Bounty == 0);
        RegistryView again = _registry.Serve("rook", Ask(RegistryRequest.Open));
        Check($"again too soon: nothing new ({again.Message})", again.Message.Contains("Nothing new") && !again.Session);

        // A pirate base's dealer
        var pirates = new StationRecord { Id = "PIRATES" };
        var callsigns = new OutfitLine { Item = GD.Load<Outfit>("res://assets/outfits/callsign.tres"), Stock = 8 };
        var spoof = new OutfitLine { Item = GD.Load<Outfit>("res://assets/outfits/spoof.tres"), Stock = 3 };
        pirates.Stock[callsigns.Id] = 8;
        pirates.Stock[spoof.Id] = 3;
        Market.BuyCarried(pirates, callsigns, pilot, universe.State, out _);
        Market.BuyCarried(pirates, callsigns, pilot, universe.State, out _);
        Market.BuyCarried(pirates, spoof, pilot, universe.State, out long spoofPrice);
        List<ItemRecord> signs = pilot.Items.Where(i => i.Kind == ItemRecord.Callsign).ToList();
        Check($"two false callsigns bought, each new and on record ({string.Join(", ", signs.Select(s => s.Name))})",
              signs.Count == 2 && signs[0].Name != signs[1].Name && signs.All(s => universe.State.Forged.ContainsKey(s.Name)));
        Check($"SPOOF bought on a diskette ({spoofPrice} credits)", pilot.Items.Any(i => i.Kind == ItemRecord.Diskette && i.Data == "spoof"));
        string sold = signs[0].Name;                    // the dealer takes back the first one carried
        Market.SellCarried(pirates, callsigns, pilot, universe.State, out _);
        Check("a callsign sold back is no more", !pilot.Items.Any(i => i.Name == sold) && !universe.State.Forged.ContainsKey(sold));
        string sign = signs[1].Name;

        // SPOOF, from drive B: to the ship's diskette, and run
        ShipComputer computer = _ship.Find<ShipComputer>();
        Check("COS is up", await Until(() => Screen().TrimEnd().EndsWith("A:\\>"), 90));
        var slot = new DiskDrive { Unit = 1 };
        _ship.AddChild(slot);                           // anywhere under the ship: drive B: of its computer
        Check("the SPOOF diskette goes into drive B:", slot.Insert(pilot.Items.First(i => i.Data == "spoof")) && computer.HasDiskB);
        await Command("COPY B:SPOOF.COM A:");
        Check("COS copies SPOOF.COM onto the ship's diskette", await Until(() => FatDisk.Read(computer.DiskImage(), "SPOOF.COM") != null && !computer.DriveLight, 60));
        slot.Insert(null);
        await Seconds(1);
        await Command("SPOOF");
        Check("SPOOF asks for the callsign", await Until(() => Screen().Contains("callsign to send"), 30));
        await Command(sign);
        Check($"the radio sends {sign} ({radio.Sending})", await Until(() => radio.Sending == sign, 10));
        await Command("");

        // Stations
        _ship.GlobalPosition = _dock.ApproachPoint;
        radio.AskDocking();
        Check($"cleared as {sign}, a docking less on it", await Until(() => radio.Dock != 0, 3) && universe.State.Forged[sign].Uses == 4);
        Check("its record here is the callsign's, not the pilot's", _security.Jurisdiction(_ship) is (_, CrimeRecord r) &&
              r == universe.State.Station(_station.Id).CrimesOf(universe.State.Forged[sign].Pilot));

        var ghost = Ship("Ghost", _dock.ApproachPoint + _dock.GlobalBasis.X * 60);
        var ghostRadio = ghost.Find<RadioDevice>();
        WriteTransponder(ghost, "NOBODY");
        await Seconds(0.1);                             // the sector's radios as of the next step
        Check($"a callsign on nobody's record ({ghostRadio.Sending}, {universe.IdentityOf(ghost).Kind})", ghostRadio.Sending == "NOBODY" && universe.IdentityOf(ghost).Kind == Identity.Unknown);
        ghostRadio.AskDocking();
        Check($"is turned away ({Last(ghostRadio)})", await Until(() => Last(ghostRadio).Contains("not on record"), 3) && ghostRadio.Dock == 0);
        universe.State.Forged["XX-0001"] = new ForgedRecord { Callsign = "XX-0001", Uses = 0 };
        WriteTransponder(ghost, "XX-0001");
        ghostRadio.AskDocking();
        await Seconds(1);
        Check("a false callsign with no dockings left is turned away too", ghostRadio.Dock == 0);

        // The door's registration reader
        _security.RegistrationRange = 300;
        _ship.GlobalPosition = _dock.GlobalTransform * new Vector3(0, 0, 200);
        Check("read at the door: the callsign is no good any more", await Until(() => universe.State.Forged[sign].Burned, 3));
        Check($"the ship is hostile, its pilot has a bounty ({universe.State.Station(_station.Id).CrimesOf("rook").Bounty})",
              _security.StandingOf(_ship) == Standing.Hostile && universe.State.Station(_station.Id).CrimesOf("rook").Bounty == _security.ForgeryBounty);
        Check("found out, it is on nobody's record", _security.Jurisdiction(_ship) == null);

        // Logged at the terminal: cleared in, the station knows who
        ShipBody honest = Ship("Honest", _dock.ApproachPoint - _dock.GlobalBasis.X * 60);
        universe.Fly(honest, "honest");
        universe.State.Pilot("honest").Ship.Hull = "res://assets/ship/hulls/hull_fighter.tres";
        honest.Find<RadioDevice>().AskDocking();
        await Until(() => honest.Find<RadioDevice>().Dock != 0, 3);
        honest.GlobalPosition = _dock.InsidePoint + _dock.GlobalBasis.X * 15;
        _registry.Serve("honest", Ask(RegistryRequest.Open));
        RegistryView tried = null;
        for (int i = 0; i < 100 && tried?.Locked != true; i++)
            tried = _registry.Serve("honest", Ask(RegistryRequest.Failed));
        Check($"wrong keys until the intrusion watch traces it: a pilot the station cleared in has a bounty ({tried.Message})",
              tried.Locked && !tried.Session && universe.State.Station(_station.Id).CrimesOf("honest").Bounty == _registry.TheftBounty);
        Check("and the terminal won't open for them", !_registry.Serve("honest", Ask(RegistryRequest.Open)).Session);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private RegistryRequest Ask(string what, IEnumerable<string> records = null) =>
        new() { Station = _station.Id, Do = what, Records = records?.ToList() ?? new() };

    private ShipBody Ship(string name, Vector3 at)
    {
        var ship = new ShipBody { Name = name, Spec = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_fighter.tres"), Position = at };
        ship.AddChild(new ShipComputer { Name = "ShipComputer", PowerOnAtStart = false });
        ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        ship.AddChild(new RadioDevice { Name = "Radio" });
        ship.AddChild(new ShipHull { Name = "Hull", Insured = false });
        ship.AddChild(new ShipSignature { Name = "Signature" });
        AddChild(ship);
        return ship;
    }

    // What SPOOF does, by hand: the callsign at the transponder's address, then the radio's command
    private static unsafe void WriteTransponder(ShipBody ship, string sign)
    {
        byte* p = ship.Computer.Memory + ShipMemory.Base + ShipMemory.Transponder;
        for (int i = 0; i < 8; i++)
            p[i] = (byte)(i < sign.Length ? sign[i] : ' ');
        ship.Computer.Bus.Out(RadioDevice.Slot, 6, 1);
        ship.Computer.Bus.Out(RadioDevice.Slot, 5, RadioDevice.TransponderCommand);
        ship.Find<RadioDevice>().AfterStep(ship, 0);
    }

    private static string Last(RadioDevice radio) => radio.Heard.Count > 0 ? radio.Heard[^1].Text : "";

    private string Screen() => _ship.Computer.ScreenText();

    private async Task Command(string line)
    {
        ShipComputer computer = _ship.Computer;
        computer.ReleaseKeys();
        foreach (char c in line + "\n")
        {
            bool shift = c == ':';
            string code = c switch
            {
                '\n' => "Enter",
                ' ' => "Space",
                '.' => "Period",
                '-' => "Minus",
                ':' => "Semicolon",
                >= 'A' and <= 'Z' => "Key" + c,
                >= '0' and <= '9' => "Digit" + c,
                _ => throw new ArgumentException($"no key for '{c}'"),
            };
            if (shift)
                computer.Key("ShiftLeft", true);
            computer.Key(code, true);
            await Seconds(0.05);
            computer.Key(code, false);
            if (shift)
                computer.Key("ShiftLeft", false);
            await Seconds(0.05);
        }
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * Engine.PhysicsTicksPerSecond; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
