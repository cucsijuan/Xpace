using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Cockpit;
using Xpace.OnFoot;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// On foot, in the game's main scene: the player's fighter docks on a pad of the station's first hangar, then:
///
///   - in the seat, the HUD offers to leave it; Leave the seat stands the pilot up beside the ship, with the walker's
///     camera, the cockpit hidden and the hull shown, the pilot's controls stopped;
///   - the walker stands on the pad, walks where the camera looks at a jog and sprints faster, its body turned
///     the way it goes and its animations told the speed; the hull stops it;
///   - the camera's arm stops short of the hangar's wall behind it;
///   - off the pad's edge, it falls to the hangar's floor; up the pad's stairs, it is back on the pad;
///   - the station hides what is behind it from the line of sight, a line beside it is clear;
///   - back beside the ship, Use boards it: the pilot's camera and HUD again, the hull hidden.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/on_foot_test.tscn (XPACE_ONFOOT_SHOTS=dir, in a window,
/// saves pictures). Exits with 0 if every check passed.
/// </summary>
public partial class OnFootTest : Node
{
    private int _failures;
    private ShipBody _ship;

    public override async void _Ready()
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Node3D>();
        AddChild(main);
        _ship = main.GetNode<ShipBody>("Ship");
        var seat = _ship.GetNode<ShipSeat>("Seat");
        var pilot = _ship.GetNode<Pilot>("Pilot");
        var radio = _ship.GetNode<RadioDevice>("Radio");
        Node3D station = main.GetNode<Node3D>("Station");
        HangarDock dock = station.GetNode("HangarBig1").GetNode<HangarDock>("Dock");

        Check("the computer boots", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 90));
        // Inside the zone, by the hangar's door, and cleared
        Place(dock.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 300)));
        await Seconds(1);
        radio.AskDocking();
        Check("cleared to a pad", await Until(() => radio.Dock == dock.Number && radio.Pad != 0, 5));
        LandingPad pad = dock.PadNumbered(radio.Pad);
        Place(pad.GlobalTransform * pad.Rest(_ship).TranslatedLocal(new Vector3(0, 1, 0)));
        Check("docked", await Until(() => pad.State == LandingPad.PadState.Docked, (float)pad.SettleSeconds + 3));
        await Seconds(0.2);
        Check($"the HUD offers to leave the seat ({pilot.Hint})", pilot.Hint.Contains("leave the seat"));
        Shot("0_seated");

        Input.ParseInputEvent(new InputEventAction { Action = OnFootInput.LeaveSeat, Pressed = true });
        await Seconds(0.1);
        Input.ParseInputEvent(new InputEventAction { Action = OnFootInput.LeaveSeat, Pressed = false });
        Walker walker = seat.Walker;
        Check("out of the seat: a walker", walker != null && IsInstanceValid(walker));
        if (walker == null)
        {
            Finish();
            return;
        }
        var cockpit = _ship.GetNode<Node3D>("Cockpit");
        var exterior = _ship.GetNode<Node3D>("Exterior");
        Check("its camera is the view; the cockpit hidden, the hull shown; the pilot stopped",
              walker.Camera.Current && !cockpit.Visible && exterior.Visible && !pilot.Seated && pilot.ProcessMode == ProcessModeEnum.Disabled);
        Check("it stands on the pad", await Until(() => walker.IsOnFloor(), 2));
        float padTop = (pad.GlobalTransform.AffineInverse() * walker.GlobalPosition).Y;
        Check($"at the pad's top ({padTop:0.00} m above the pad's frame)", Mathf.Abs(padTop) < 0.6f);
        Check($"next to the ship, it may board ({walker.Prompt})", walker.Prompt.Contains("board"));
        await Seconds(0.5);
        Shot("1_out");

        // Another player on foot, as a session shows them: a few steps ahead, facing this one, their name overhead
        Node3D theirStation = Xpace.Space.StationRadio.Above(walker).GetParent<Node3D>();
        Vector3 floorUp = walker.GlobalBasis.Y.Normalized();
        Vector3 forward = -walker.Camera.GlobalBasis.Z;
        forward = (forward - floorUp * forward.Dot(floorUp)).Normalized();
        Transform3D toStation = theirStation.GlobalTransform.AffineInverse();
        var other = Xpace.OnFoot.RemoteWalker.Make(GD.Load<PackedScene>("res://scenes/characters/crew_body.tscn"), "ACE");
        theirStation.AddChild(other);
        other.Hear(new Xpace.Net.WalkerState
        {
            Station = "HAVEN", Grounded = true,
            Position = toStation * (walker.GlobalPosition + forward * 3),
            Facing = (toStation.Basis * Basis.LookingAt(-forward, floorUp)).Orthonormalized().GetRotationQuaternion(),
        });
        await Seconds(0.5);
        Check("another player on foot stands where their machine says, on the same floor",
              other.GlobalPosition.DistanceTo(walker.GlobalPosition + forward * 3) < 0.01f && other.Body != null && other.Pilot == "ACE");
        Shot("1b_another_player");
        other.QueueFree();

        // Down on the hangar's floor, between the door and the pads, jog and sprint toward the pads
        Node3D hangar = dock.GetParentNode3D();
        walker.GlobalPosition = hangar.ToGlobal(new Vector3(0, 0.5f, -25));
        walker.LookYaw = 0;                     // the hangar's -Z
        await Seconds(0.5);
        Vector3 start = walker.GlobalPosition;
        Input.ActionPress(OnFootInput.Forward);
        await Seconds(1.0);
        float jog = Flat(walker.Velocity, walker).Length();
        Input.ActionPress(OnFootInput.Sprint);
        await Seconds(0.8);
        float sprint = Flat(walker.Velocity, walker).Length();
        Shot("2_running");
        Input.ActionRelease(OnFootInput.Sprint);
        Input.ActionRelease(OnFootInput.Forward);
        Vector3 went = walker.GlobalPosition - start;
        Vector3 ahead = -hangar.GlobalBasis.Z.Normalized();
        Check($"it jogs ({jog:0.0} m/s) and sprints faster ({sprint:0.0} m/s)", Mathf.Abs(jog - walker.JogSpeed) < 0.3f && sprint > jog + 1.5f);
        Check($"where the camera looks ({went.Length():0.0} m, {Mathf.RadToDeg(went.AngleTo(ahead)):0} degrees off)",
              went.Length() > 3 && went.AngleTo(ahead) < 0.1f);
        Vector3 facing = walker.Body.GlobalBasis * Vector3.Forward;
        Check($"its body faces the way it went ({Mathf.RadToDeg(facing.AngleTo(went)):0} degrees off), animated at {walker.Body.Speed:0.0} m/s",
              facing.AngleTo(went) < 0.3f && walker.Body.Speed > 3);
        await Seconds(1);

        // Straight at the nose: the hitboxes stop it
        Vector3 nose = _ship.ToGlobal(new Vector3(0, _ship.Bounds.Position.Y + 0.2f, _ship.Bounds.Position.Z - 2));
        walker.GlobalPosition = nose;
        await Seconds(0.3);
        Vector3 back = walker.GlobalBasis.Inverse() * _ship.GlobalBasis.Z;
        walker.LookYaw = Mathf.Atan2(-back.X, -back.Z);
        Input.ActionPress(OnFootInput.Forward);
        await Seconds(1.5);
        Input.ActionRelease(OnFootInput.Forward);
        float into = _ship.ToLocal(walker.GlobalPosition).Z - _ship.Bounds.Position.Z;
        Check($"the hull stops it ({into:0.0} m past the nose's tip)", into < 0.5f);

        // The camera's arm against the hangar's back wall
        Vector3 wall = walker.Hit(hangar.ToGlobal(new Vector3(0, 1.5f, -40)), -hangar.GlobalBasis.Z.Normalized());
        walker.GlobalPosition = wall + hangar.GlobalBasis.Z.Normalized() * 0.6f - hangar.GlobalBasis.Y.Normalized() * 1.2f;
        walker.LookYaw = Mathf.Pi;              // looking out of the hangar: the camera behind, against the wall
        walker.LookPitch = 0;
        await Seconds(1);
        float arm = walker.Camera.GlobalPosition.DistanceTo(walker.Pitch.GlobalPosition);
        Shot("3_wall");
        Check($"its back to the wall: the camera comes in ({arm:0.0} m from the head)", arm < 2.0f);
        Check($"down on the hangar's floor ({hangar.ToLocal(walker.GlobalPosition).Y:0.0} m)", walker.IsOnFloor() && hangar.ToLocal(walker.GlobalPosition).Y < padTop + pad.Position.Y - 1);

        // Off the pad's edge: down to the floor
        walker.GlobalPosition = pad.ToGlobal(new Vector3(11, 0.3f, 0));
        await Seconds(0.3);
        Vector3 outward = walker.GlobalBasis.Inverse() * pad.GlobalBasis.X;
        walker.LookYaw = Mathf.Atan2(-outward.X, -outward.Z);
        Input.ActionPress(OnFootInput.Forward);
        await Seconds(1.2);
        Input.ActionRelease(OnFootInput.Forward);
        await Seconds(1.0);
        float drop = (pad.GlobalTransform.AffineInverse() * walker.GlobalPosition).Y;
        Shot("4_floor");
        Check($"off the pad's edge it falls to the floor ({drop:0.0} m below the pad's top)", drop < -1.5f && walker.IsOnFloor());

        // Up the pad's stairs, from the floor
        walker.GlobalPosition = pad.ToGlobal(new Vector3(-4, -3.0f, -19.5f));
        await Seconds(0.3);
        Vector3 inward = walker.GlobalBasis.Inverse() * pad.GlobalBasis.Z;
        walker.LookYaw = Mathf.Atan2(-inward.X, -inward.Z);
        walker.LookPitch = -0.3f;
        Input.ActionPress(OnFootInput.Forward);
        await Seconds(1.5);
        Shot("5_stairs");
        await Seconds(1.5);
        Input.ActionRelease(OnFootInput.Forward);
        await Seconds(0.5);
        Vector3 up = pad.ToLocal(walker.GlobalPosition);
        Check($"up the stairs, back on the pad ({-up.Z:0.0} m out, {up.Y:0.00} m up)", -up.Z < 12.5f && Mathf.Abs(up.Y) < 0.3f && walker.IsOnFloor());

        // Radar: the station between two points, and a line beside it
        Vector3 c = station.GlobalPosition;
        Vector3 across = station.GlobalBasis.X.Normalized() * 2000;
        Check("the station hides what is behind it", Obstacles.Blocked(c - across, c + across));
        Vector3 above = station.GlobalBasis.Y.Normalized() * 1200;
        Check("a line clear of it is not hidden", !Obstacles.Blocked(c - across + above, c + across + above));

        // Back to the ship and board
        walker.GlobalPosition = seat.ExitPoint;
        await Seconds(0.3);
        Input.ParseInputEvent(new InputEventAction { Action = OnFootInput.Interact, Pressed = true });
        await Seconds(0.1);
        Input.ParseInputEvent(new InputEventAction { Action = OnFootInput.Interact, Pressed = false });
        await Seconds(0.2);
        Check("boarded: the pilot's camera and HUD, the cockpit shown, the hull hidden",
              seat.Walker == null && pilot.Seated && pilot.GetNode<Camera3D>("Camera3D").Current && cockpit.Visible && !exterior.Visible);
        Check("still docked", pad.State == LandingPad.PadState.Docked);
        Finish();
    }

    private static Vector3 Flat(Vector3 v, Node3D frame)
    {
        Vector3 local = frame.GlobalBasis.Inverse() * v;
        return new Vector3(local.X, 0, local.Z);
    }

    private void Place(Transform3D at) =>
        _ship.State = new ShipState { Position = at.Origin, Orientation = at.Basis.GetRotationQuaternion() };

    private void Finish()
    {
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Shot(string name)
    {
        string dir = System.Environment.GetEnvironmentVariable("XPACE_ONFOOT_SHOTS");
        if (string.IsNullOrEmpty(dir) || DisplayServer.GetName() == "headless")
            return;
        GetViewport().GetTexture().GetImage().SavePng($"{dir}/{name}.png");
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }

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
}
