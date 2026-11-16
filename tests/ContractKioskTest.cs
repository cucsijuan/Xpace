using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Cockpit;
using Xpace.Menu;
using Xpace.OnFoot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// The contracts terminal in the real game:
///   - a new pilot's journal has the story's first step, and the guide shows where to get it;
///   - the guide key says it was read: it hides and stays hidden;
///   - docked at Alpha, the walker at the contracts kiosk opens its window, which offers the step, marked as the story's;
///   - taken there, it is the pilot's, the guide shows how to do it, and the journal page lists it.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/contract_kiosk_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class ContractKioskTest : Node
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
        var seat = _ship.GetNode<ShipSeat>("Seat");
        var radio = _ship.Find<RadioDevice>();
        var guide = main.GetNode<GuideWindow>("GuideWindow");
        Node3D station = main.Sector.GetNode<Node3D>("MiltecAlpha");
        HangarDock dock = station.GetNode("HangarBig1").GetNode<HangarDock>("Dock");
        var kiosk = dock.GetParent().GetNode<StationKiosk>("ContractKiosk");

        Check("the computer boots", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 90));
        PilotRecord me = universe.Me;
        Check($"the journal has the story's first step ({me.Journal.FirstOrDefault()?.Title})", await Until(() => me.Journal.Any(j => j.Story && j.State == ContractRecord.Offered), 12));
        Check($"the guide shows where to get it ({guide.Shown?.Title})", await Until(() => guide.Shown?.Help.Contains("contracts terminal") == true, 2));
        await Press(FlightInput.Guide);
        Check("the guide key: read, hidden", guide.Shown == null && await Until(() => me.GuidesRead.Count == 1, 1));
        await Seconds(1.5);
        Check("and it doesn't come back", guide.Shown == null);

        Place(dock.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 300)));
        await Seconds(1);
        radio.AskDocking();
        Check("cleared to a pad", await Until(() => radio.Dock == dock.Number && radio.Pad != 0, 5));
        LandingPad pad = dock.PadNumbered(radio.Pad);
        Place(pad.GlobalTransform * pad.Rest(_ship).TranslatedLocal(new Vector3(0, 1, 0)));
        Check("docked", await Until(() => station.GetNode<StationRadio>("Radio").DockedHere(_ship), (float)pad.SettleSeconds + 3));
        await Press(OnFootInput.LeaveSeat);
        Walker walker = seat.Walker;
        if (walker == null)
        {
            Check("out of the seat", false);
            Finish();
            return;
        }
        Node3D front = kiosk.GetNode<Node3D>("Front");
        walker.GlobalPosition = front.GlobalPosition + front.GlobalBasis.Y * 0.3f;
        walker.LookYaw = Mathf.Pi;
        await Seconds(0.5);
        Check($"at the kiosk ({walker.Prompt})", walker.Prompt.Contains("contracts") && walker.Offered == kiosk);
        await Press(OnFootInput.Interact);
        await Seconds(0.3);
        var window = kiosk.Window as ContractWindow;
        Check("using it opens the contract board", window != null && walker.Busy);
        if (window == null)
        {
            Finish();
            return;
        }
        OfferLine offer = window.View?.Offers.FirstOrDefault(o => o.Story);
        Check($"it offers the story's step ({offer?.Title}, {offer?.Reward} cr)", offer != null && window.View.Present);
        window.Ask(ContractRequest.Take, offer?.Id ?? "");
        await Seconds(0.3);
        Check($"taken: the pilot's ({window.View?.Message})", window.View?.Mine.Any(m => m.Id == offer?.Id) == true && !window.View.Offers.Any(o => o.Id == offer?.Id));
        Check("the guide shows how to do it", await Until(() => guide.Shown?.Help.Contains("Quartz") == true, 2));
        window.Close();
        await Seconds(0.2);
        Check("closed, the walker is free", !walker.Busy);
        Check($"the journal has it taken ({me.Journal.FirstOrDefault()?.Objective})", me.Journal.FirstOrDefault()?.State == ContractRecord.Taken && me.Journal[0].Objective.Contains("Collect"));
        Finish();
    }

    private async Task Press(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        await Seconds(0.1);
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        await Seconds(0.1);
    }

    private void Place(Transform3D at) =>
        _ship.State = new ShipState { Position = at.Origin, Orientation = at.Basis.GetRotationQuaternion() };

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
