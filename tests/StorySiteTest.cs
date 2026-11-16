using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Controls;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// The story's wreck in the real game:
///   - the second step taken, the wreck is there, at the contract's place, and its distress beacon is on the ship's
///     radio from the station, with the wreck's callsign;
///   - from the station the wreck is no echo; near, it is, as big as the freighter's, with no heat; it is solid;
///   - the giver's cargo drifts round it in pods: flown through, they come aboard consigned; one holds the black box,
///     which the pilot carries away;
///   - the black box in drive B:, COS types its flight log, which names the ship the third step asks for;
///   - the step done, the wreck and whatever was left of it are gone.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/story_site_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class StorySiteTest : Node
{
    private int _failures;
    private ShipBody _ship;

    public override async void _Ready()
    {
        Universe universe = Universe.Current;
        universe.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        _ship = main.Ship;
        var sites = main.GetNode<StorySites>("StorySites");
        var radio = _ship.Find<RadioDevice>();
        Check("the computer boots", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 90));
        PilotRecord me = universe.Me;
        me.Story["borrowed_signature"] = 1;
        ContractRecord step = null;
        Check("the second step is offered", await Until(() => (step = universe.State.Contracts.Values.FirstOrDefault(c => c.For == me.Id && c.Step == 1 && c.Open)) != null, 12));
        if (step == null)
        {
            Finish();
            return;
        }
        Check("taken", Contracts.Take(universe.State, step.Id, me.Id, "ALPHA", universe.Account) == null);
        Node3D wreck = null;
        Check($"the wreck of {step.Wreck} is there", await Until(() => (wreck = sites.WreckOf(step.Id)) != null, 3));
        var origin = FloatingOrigin.Of(_ship);
        SectorPoint at = origin.Origin + origin.ToLocal(wreck.GlobalPosition);
        Check($"at the contract's place ({at})", new Vector3((float)(at.X - step.X), (float)(at.Y - step.Y), (float)(at.Z - step.Z)).Length() < 1);
        Check($"its distress beacon is on the radio from the station ({_ship.GlobalPosition.DistanceTo(wreck.GlobalPosition) / 1000:0} km)",
              await Until(() => radio.Contacts.Any(c => c is DistressBeacon && c.Callsign == step.Wreck && c.Kind == 'V'), 5));

        var scope = _ship.Find<RadarDevice>();
        WreckEcho echo = wreck.GetNode<WreckEcho>("Echo");
        await Seconds(3);
        Check("from the station it is no echo", !scope.Echoes.Any(e => e.Id == echo.RadarId));

        // Out to the wreck, through its pods
        Place(wreck.GlobalPosition + new Vector3(0, 0, 400));
        await Seconds(1);
        Node freighter = GD.Load<PackedScene>("res://scenes/ships/ai_freighter.tscn").Instantiate();
        float hull = freighter.GetNode<ShipSignature>("Signature").RadarCrossSection;
        freighter.Free();
        Check($"near, it is an echo, as big as the freighter's ({echo.RadarCrossSection} m²)", await Until(() => scope.Echoes.Any(e => e.Id == echo.RadarId), 6) && echo.RadarCrossSection == hull);
        Check("cold: no heat", !wreck.FindChildren("*", "", true, false).OfType<IHeatSource>().Any());
        Check("solid", Solid.All.Any(s => wreck.IsAncestorOf(s) && s.A.Length > 0));
        CargoPod[] pods = Pods(step);
        Check($"{pods.Length} pods round it: {pods.Where(p => p.Item == null).Sum(p => p.Tonnes)} t of chips and the black box",
              pods.Where(p => p.Item == null).Sum(p => p.Tonnes) == step.Tonnes && pods.Count(p => p.Item != null) == 1);
        foreach (CargoPod pod in pods)
        {
            Place(pod.GlobalPosition + new Vector3(0, 0, 10));
            await Until(() => !IsInstanceValid(pod) || pod.Tonnes == 0 && pod.Item == null, 3);
        }
        Check($"the chips are aboard, consigned ({me.Ship.ConsignedOf("chips")} t)", me.Ship.ConsignedOf("chips") == step.Tonnes && me.Ship.StolenOf("chips") == 0 && step.Loaded == step.Tonnes);
        ItemRecord box = me.Items.FirstOrDefault(i => i.Name == step.Item.Name);
        Check($"the pilot carries the black box ({box?.Name})", box != null);
        await Seconds(1.5);
        Check("nothing more drifts there", Pods(step).Length == 0);

        // The black box, read under COS
        Check("COS is up", await Until(() => _ship.Computer.ScreenText().Contains("A:\\>"), 30));
        DiskDrive drive = _ship.FindChildren("*", "", true, false).OfType<DiskDrive>().FirstOrDefault(d => d.Unit == 1);
        Check("the black box goes into drive B:", drive != null && box != null && drive.Insert(box) && _ship.Computer.HasDiskB);
        await Command("TYPE B:FLIGHT.LOG");
        string borrowed = me.Facts.GetValueOrDefault("borrowed_signature.borrowed");
        Check($"COS types its log, naming {borrowed}", await Until(() => _ship.Computer.ScreenText().Contains($"{borrowed} ON RADIO"), 30));
        GD.Print(_ship.Computer.ScreenText());

        // Done: the site goes
        Contracts.HandOver(universe.State, universe.Book, step.Id, me.Id, me.Ship, me, "ALPHA", universe.Account);
        Check("handed over: the step is done", step.State == ContractRecord.Done);
        ContractRecord next = null;
        Check("the third step is offered", await Until(() => (next = universe.State.Contracts.Values.FirstOrDefault(c => c.For == me.Id && c.Step == 2 && c.Open)) != null, 12));
        Check("the wreck is gone while nobody looks for it", await Until(() => sites.WreckOf(step.Id) == null, 3));
        Finish();
    }

    private CargoPod[] Pods(ContractRecord step) =>
        GetTree().GetNodesInGroup(CargoPod.Group).OfType<CargoPod>().Where(p => p.Site == step.Id && !p.IsQueuedForDeletion()).ToArray();

    private void Place(Vector3 at) => _ship.State = new ShipState { Position = at, Orientation = Quaternion.Identity };

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
                ':' => "Semicolon",
                >= 'A' and <= 'Z' => "Key" + c,
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
