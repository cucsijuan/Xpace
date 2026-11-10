using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.OnFoot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// A station's registry terminal, worked by typing on its computer as a player does:
///
///   - it boots COS into REGISTRY, which shows the station's real traffic logbook (the ships that docked lately);
///   - Alt+F10 opens the service session, behind the service key; a wrong key says how long the check took, and
///     each matching digit makes it longer: the key is found a digit at a time from the times alone;
///   - in the core's dump a signature carved where a good record starts is copied, anywhere else it is a bad record
///     that the intrusion watch counts; written, the signatures go into the ship's hold as registry data;
///   - wrong keys until the intrusion watch traces the session close it and lock the service; a ship that says nothing
///     of who it is is noticed by its hangar's watch.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/registry_terminal_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class RegistryTerminalTest : Node3D
{
    private StationTerminal _terminal;
    private ShipComputer _pc;
    private RegistryDevice _card;
    private int _failures;

    public override async void _Ready()
    {
        Universe universe = Universe.Current;
        universe.Forget();
        AddChild(new Projectiles());
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        AddChild(station);
        var radio = station.GetNode<StationRadio>("Radio");
        var security = station.GetNode<StationSecurity>("Security");
        var registry = station.GetNode<StationRegistry>("Registry");
        security.Scans = false;
        security.PoliceCount = 0;
        Node hangar = station.GetNode("HangarBig1");
        var dock = hangar.GetNode<HangarDock>("Dock");
        _terminal = hangar.FindChildren("*", "", true, false).OfType<StationTerminal>().First();
        _terminal.AlwaysOn = true;
        _pc = _terminal.Computer;
        _card = _terminal.Registry;

        // The pilot's ship, in the hangar, its radio off: nobody can tell it
        var ship = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
        ship.GetNode("Pilot").Free();
        ship.Position = dock.InsidePoint;
        AddChild(ship);
        ship.GlobalBasis = dock.GlobalBasis;
        universe.Fly(ship, universe.LocalPilotId);
        PilotRecord pilot = universe.Me;
        pilot.Ship.Hull = "res://assets/ship/hulls/hull_fighter.tres";
        pilot.Ship.Empty();
        ship.Find<RadioDevice>().Powered = false;
        StationRecord record = universe.State.Station(radio.Id);
        Registries.Log(record, "FR-1A2B", universe.State.Clock - 600, 2, 1);
        Registries.Log(record, "QZ-7781", universe.State.Clock - 120, 1, 2);

        // The logbook
        Check("it boots into the traffic register", await Until(() => Screen().Contains("TRAFFIC REGISTER") && Screen().Contains("TERMINAL READY"), 60));
        string screen = Screen();
        Check("the logbook shows the ships that docked, newest first",
              screen.IndexOf("QZ-7781", StringComparison.Ordinal) is int a and >= 0 && screen.IndexOf("FR-1A2B", StringComparison.Ordinal) > a &&
              Regex.IsMatch(screen, @"QZ-7781\s+1\s+2\s+2 MIN AGO"));

        // The service session and its key
        await Chord("AltLeft", "F10");
        Check("Alt+F10: the service session asks for its key", await Until(() => Screen().Contains("SERVICE KEY"), 10));
        string key = _card.ServiceKey;
        int tries = 0;
        string found = "";
        bool inside = false;
        // As a player does: the digits one after another until one takes clearly longer than the rest (or clearly
        // shorter: the first one tried was it)
        for (int round = 0; round < key.Length + 3 && !inside && found.Length < key.Length; round++)
        {
            var seen = new Dictionary<char, int>();
            foreach (char c in "0123456789ABCDEF")
            {
                await Type(c.ToString());
                await Press("Enter");
                tries++;
                if (await Until(() => Screen().Contains("REGISTRY CORE"), 0.5))
                {
                    found += c;
                    inside = true;
                    break;
                }
                seen[c] = Response();
                await Press("Backspace");
                if (seen.Values.Max() - seen.Values.Min() > 260)
                {
                    char pick = seen.MaxBy(p => p.Value).Key;
                    found += pick;
                    await Type(pick.ToString());
                    break;
                }
            }
        }
        Check($"the key found from the times alone: {found} (is {key}) in {tries} tries, trace {_card.Trace:0}/{_card.View.TraceLimit}",
              inside && found == key && _card.Trace < _card.View.TraceLimit);

        // The core
        int[] good = _card.Live.Keys.OrderBy(o => o).ToArray();
        int bad = Enumerable.Range(0, 64).Select(r => r * 16).First(o => !_card.Live.ContainsKey(o));
        int row = 0;
        row = await Move(row, bad / 16);
        double before = _card.Trace;
        await Press("KeyC");
        bool counted = await Until(() => Screen().Contains("NO GOOD RECORD") && _card.Trace >= before + registry.TracePerBadRecord - 0.1, 2);
        Check($"a bad record carved: counted by the watch ({before:0} -> {_card.Trace:0})", counted);
        int want = registry.RecordsPerTonne * 2;
        foreach (int offset in good.Take(want))
        {
            row = await Move(row, offset / 16);
            await Press("KeyC");
            await Until(() => Screen().Contains("SIGNATURE CARVED"), 2);
        }
        Check($"{want} signatures carved ({_card.CarvedRecords.Count})", _card.CarvedRecords.Count == want);
        await Press("KeyW");
        Check("written: 2 t of registry data in the hold, the session closed",
              await Until(() => pilot.Ship.Holds("registry_data") == 2 && Screen().Contains("SESSION CLOSED"), 5));
        await Press("Space");

        // Traced
        record.RegistryCopied.Remove(pilot.Id);
        await Until(() => Screen().Contains("TRAFFIC REGISTER"), 5);
        await Chord("AltLeft", "F10");
        await Until(() => Screen().Contains("SERVICE KEY"), 10);
        await Type("0");
        for (int i = 0; i < 80 && !Screen().Contains("TRACED"); i++)
            await Press("Enter");
        Check("wrong keys until the watch traces the session", await Until(() => Screen().Contains("CONNECTION TRACED"), 5));
        Check("a ship nobody can tell: the hangar's watch has noticed it", dock.Watch.NoticedAgo(ship) != null);
        await Press("Space");
        await Chord("AltLeft", "F10");
        Check("the service is locked for the pilot", await Until(() => Screen().Contains("SERVICE LOCKED"), 10));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private string Screen() => _pc.ScreenText();

    private int Response()
    {
        Match m = Regex.Match(Screen(), @"RESPONSE\s+(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }

    // The cursor from one row of the dump to another
    private async Task<int> Move(int from, int to)
    {
        while (to - from >= 20)
        {
            await Press("PageDown");
            from += 20;
        }
        while (from < to)
        {
            await Press("ArrowDown");
            from++;
        }
        while (from > to)
        {
            await Press("ArrowUp");
            from--;
        }
        return from;
    }

    private async Task Type(string text)
    {
        foreach (char c in text)
            await Press(char.IsDigit(c) ? "Digit" + c : "Key" + c);
    }

    private async Task Press(string code)
    {
        _pc.Key(code, true);
        await Frames(2);
        _pc.Key(code, false);
        await Frames(3);
    }

    private async Task Chord(params string[] codes)
    {
        foreach (string c in codes)
        {
            _pc.Key(c, true);
            await Frames(2);
        }
        foreach (string c in codes.Reverse())
            _pc.Key(c, false);
        await Frames(3);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * 60; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return done();
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
