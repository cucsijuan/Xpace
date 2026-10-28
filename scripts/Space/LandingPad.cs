using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.Ship;

namespace Xpace.Space;

/// <summary>
/// A landing pad in a hangar (below a <see cref="HangarDock"/>, anywhere in
/// the hangar's scene). Its frame: the origin on the pad's top, +Y up, -Z
/// the way a docked ship's nose points.
///
/// When traffic control clears a ship to the pad's hangar and to this pad
/// (one that fits it, <see cref="Fits"/>, and is no one else's), the pad's
/// glow lights red; only that pad lights, and only that pad takes the ship. Over the pad (within <see cref="Leeway"/> of its middle), turned
/// as it is (within <see cref="HeadingLeeway"/> of its heading, its up
/// within <see cref="TiltLeeway"/> of the pad's) it turns green: all that
/// is left is to come down. Within <see cref="CaptureHeight"/> of where it
/// rests, slow (<see cref="CaptureSpeed"/>, <see cref="CaptureSpin"/>), the
/// clamps take it (<see cref="ShipBody.Holder"/>) and bring it down, slowly
/// (<see cref="SettleSeconds"/>), to its place: its lowest point
/// <see cref="Clearance"/> above the pad, lined up with it. Then it is
/// docked, and the station shuts the hangar's door.
///
/// To leave, the pilot tells the station (the radio's launch request): the
/// door opens, and once it is opening the clamps lift the ship to
/// <see cref="LaunchHeight"/> (<see cref="LaunchSeconds"/>) and let it go.
///
/// Only the machine that flies a ship docks it (in a session, the ship's
/// owner): the others see it where its owner says it is. The pad's radio
/// beacon tells the ship's radio where the pad is (<see cref="Guide"/>), for
/// the ship's programs to show.
///
/// The glow: the lights (their colour) and meshes (the instance shader
/// parameter <c>glow_color</c>, as pad_glow.gdshader has) below the pad's
/// child named <see cref="GlowNode"/>, hidden while the pad waits for
/// nobody.
/// </summary>
public partial class LandingPad : Node3D, IShipHold
{
    public enum PadState { Idle, Waiting, Aligned, Holding, Docked, Launching }

    /// <summary>Its number in its hangar (0: in the order the hangar finds its pads).</summary>
    [Export] public int Number;

    /// <summary>The ships it takes: at most this wide (x) and long (z), metres.</summary>
    [Export] public Vector2 MaxFootprint = new(24, 24);
    /// <summary>And at most this tall.</summary>
    [Export] public float MaxHeight = 12f;
    /// <summary>Between the pad's top and a docked ship's lowest point, metres.</summary>
    [Export] public float Clearance = 0.3f;

    [ExportGroup("Leeway")]
    /// <summary>How far from the pad's middle a ship's middle may be (across), metres.</summary>
    [Export] public float Leeway = 3f;
    [Export(PropertyHint.Range, "0,90")] public float HeadingLeeway = 10f;
    [Export(PropertyHint.Range, "0,90")] public float TiltLeeway = 8f;
    /// <summary>How far above its resting place a lined-up ship must come down for the clamps to take it, metres.</summary>
    [Export] public float CaptureHeight = 6f;
    /// <summary>The most a ship may be moving (metres a second) and turning (degrees a second) for the clamps.</summary>
    [Export] public float CaptureSpeed = 2.5f;
    [Export] public float CaptureSpin = 8f;

    [ExportGroup("Clamps")]
    /// <summary>How long the clamps take to bring a ship down to its place.</summary>
    [Export] public double SettleSeconds = 6;
    /// <summary>How high they lift it to let it go, above its place, metres; and how long that takes.</summary>
    [Export] public float LaunchHeight = 6f;
    [Export] public double LaunchSeconds = 4;
    /// <summary>How far the beacon's guidance reaches, metres.</summary>
    [Export] public float GuidanceRange = 1500f;

    [ExportGroup("Glow")]
    /// <summary>The child the glow's lights and meshes are below (a name, not a path: duplicating the pad keeps it its own).</summary>
    [Export] public string GlowNode = "Glow";
    [Export] public Color WaitingColor = new(1f, 0.08f, 0.04f);
    [Export] public Color AlignedColor = new(0.1f, 1f, 0.3f);
    [Export] public Color HoldingColor = new(0.1f, 1f, 0.3f);
    [Export] public Color DockedColor = new(0.15f, 0.45f, 1f);
    [Export] public Color LaunchingColor = new(1f, 0.6f, 0.1f);

    public PadState State { get; private set; }

    /// <summary>The hangar it is in (the nearest dock above it).</summary>
    public HangarDock Dock { get; private set; }

    /// <summary>The ship its clamps hold, or null.</summary>
    public ShipBody HeldShip { get; private set; }

    /// <summary>The ship it is waiting for (or holds), or null.</summary>
    public Node3D Expected { get; private set; }

    /// <summary>The ship the station here cleared to it (set by the station; null: none).</summary>
    public Node3D AssignedTo { get; set; }

    /// <summary>What the beacon tells a ship: where the pad is from it and how it must turn.</summary>
    public struct Guidance
    {
        /// <summary>Where the ship's middle goes, from it, in the ship's axes: right, up, forward (metres).</summary>
        public Vector3 Offset;
        /// <summary>How the ship must turn to lie as the pad: yaw right, pitch up, roll right (radians).</summary>
        public float Yaw, Pitch, Roll;
        public bool Aligned, Low, Held, Docked;
    }

    private readonly List<(GeometryInstance3D Mesh, Light3D Light)> _glows = new();
    private Transform3D _from;
    private double _t;
    private PadState _shown = (PadState)(-1);
    private static readonly Dictionary<ulong, Aabb> s_bounds = new();

    public override void _Ready()
    {
        for (Node n = GetParent(); n != null && Dock == null; n = n.GetParent())
            foreach (Node c in n.GetChildren())
                if (c is HangarDock d)
                {
                    Dock = d;
                    break;
                }
        if (Dock == null)
            GD.PushWarning($"{GetPath()}: a landing pad must be in a hangar with a HangarDock");
        if (GetNodeOrNull(GlowNode) is Node glow)
            foreach (Node n in glow.FindChildren("*", "", true, false).Prepend(glow))
            {
                if (n is GeometryInstance3D g)
                    _glows.Add((g, null));
                else if (n is Light3D l)
                    _glows.Add((null, l));
            }
        ShowGlow();
    }

    /// <summary>The ship fits on it.</summary>
    public bool Fits(Node3D ship)
    {
        Vector3 size = BoundsOf(ship).Size;
        return size.X <= MaxFootprint.X && size.Z <= MaxFootprint.Y && size.Y <= MaxHeight;
    }

    /// <summary>Where the ship rests on it, in the pad's frame.</summary>
    public Transform3D Rest(Node3D ship) => new(Basis.Identity, new Vector3(0, Clearance - BoundsOf(ship).Position.Y, 0));

    /// <summary>The ship is in its place on the pad (as any machine sees it: a held ship, or another machine's that is there).</summary>
    public bool Docks(Node3D ship)
    {
        if (ship == null || !IsInstanceValid(ship))
            return false;
        if (HeldShip == ship)
            return State == PadState.Docked;
        Transform3D local = GlobalTransform.AffineInverse() * ship.GlobalTransform;
        return local.Origin.DistanceTo(Rest(ship).Origin) < 0.5f && Tilt(local.Basis) < 0.05f && Mathf.Abs(Heading(local.Basis)) < 0.05f;
    }

    /// <summary>What the beacon tells the ship (false: too far, or not its pad).</summary>
    public bool Guide(ShipBody ship, out Guidance g)
    {
        g = default;
        if (ship == null || ship != Expected)
            return false;
        Transform3D rest = GlobalTransform * Rest(ship);
        Vector3 d = rest.Origin - ship.GlobalPosition;
        if (d.Length() > GuidanceRange)
            return false;
        Basis toShip = ship.GlobalBasis.Orthonormalized().Inverse();
        Vector3 local = toShip * d;
        g.Offset = new Vector3(local.X, local.Y, -local.Z);
        Vector3 forward = toShip * -rest.Basis.Z;
        Vector3 up = toShip * rest.Basis.Y;
        g.Yaw = Mathf.Atan2(forward.X, -forward.Z);
        g.Pitch = Mathf.Atan2(forward.Y, new Vector2(forward.X, forward.Z).Length());
        g.Roll = Mathf.Atan2(up.X, up.Y);
        Look(ship, out g.Aligned, out g.Low, out _);
        g.Held = HeldShip == ship;
        g.Docked = g.Held && State == PadState.Docked;
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Dock == null)
            return;
        if (HeldShip != null && (!IsInstanceValid(HeldShip) || HeldShip.Wrecked || HeldShip.Holder != this))
            Let(go: false);
        if (HeldShip != null)
        {
            if (State == PadState.Docked && WantsOut(HeldShip) && Dock.IsOpen)
            {
                State = PadState.Launching;
                _from = GlobalTransform.AffineInverse() * HeldShip.GlobalTransform;
                _t = 0;
            }
            ShowGlow();
            return;
        }

        Expected = Waiting(out ShipBody local);
        State = PadState.Idle;
        if (Expected != null)
        {
            Look(Expected, out bool aligned, out bool low, out bool slow);
            State = aligned ? PadState.Aligned : PadState.Waiting;
            if (local != null && WantsOut(local))
                State = PadState.Launching;         // let go, on its way out
            else if (local == null && Docks(Expected))
                State = PadState.Docked;            // another machine's ship, in its place
            else if (local != null && aligned && low && slow)
                Take(local);
        }
        ShowGlow();
    }

    ShipState IShipHold.Hold(ShipBody ship, double delta)
    {
        _t += delta;
        Transform3D rest = Rest(ship);
        Transform3D at = rest;
        if (State == PadState.Holding)
        {
            float k = Smooth(_t / Math.Max(SettleSeconds, 0.01));
            at = _from.InterpolateWith(rest, k);
            if (k >= 1)
                State = PadState.Docked;
        }
        else if (State == PadState.Launching)
        {
            Transform3D lifted = rest.Translated(new Vector3(0, LaunchHeight, 0));
            float k = Smooth(_t / Math.Max(LaunchSeconds, 0.01));
            at = _from.InterpolateWith(lifted, k);
            if (k >= 1)
                Let(go: true);
        }
        Transform3D world = GlobalTransform * at;
        return new ShipState
        {
            Position = world.Origin,
            Orientation = world.Basis.Orthonormalized().GetRotationQuaternion(),
            Velocity = Vector3.Zero,
            AngularVelocity = Vector3.Zero,
        };
    }

    // The ship that waits for this pad: a ship of this machine whose radio was cleared to it (it can be taken), or
    // the one the station here cleared to it (another machine's: only shown)
    private Node3D Waiting(out ShipBody local)
    {
        local = null;
        foreach (Node n in GetTree().GetNodesInGroup(Radio.Group))
            if (n is RadioDevice { Dock: > 0 } r && r.Dock == Dock.Number && r.Pad == Number && r.DockFrom == Dock.Station &&
                Combat.ShipOf(r) is ShipBody ship && !ship.Wrecked && !NetBody.IsPuppet(ship))
            {
                local = ship;
                return ship;
            }
        if (AssignedTo != null && !IsInstanceValid(AssignedTo))
            AssignedTo = null;
        return AssignedTo;
    }

    private void Look(Node3D ship, out bool aligned, out bool low, out bool slow)
    {
        Transform3D local = GlobalTransform.AffineInverse() * ship.GlobalTransform;
        Vector3 off = local.Origin - Rest(ship).Origin;
        aligned = new Vector2(off.X, off.Z).Length() <= Leeway &&
                  Mathf.Abs(Heading(local.Basis)) <= Mathf.DegToRad(HeadingLeeway) &&
                  Tilt(local.Basis) <= Mathf.DegToRad(TiltLeeway);
        low = off.Y <= CaptureHeight && off.Y >= -CaptureHeight;
        slow = ship is ShipBody body && body.LinearVelocity.Length() <= CaptureSpeed &&
               body.AngularVelocity.Length() <= Mathf.DegToRad(CaptureSpin);
    }

    private void Take(ShipBody ship)
    {
        HeldShip = ship;
        Expected = ship;
        ship.Holder = this;
        ship.LinearVelocity = Vector3.Zero;
        ship.AngularVelocity = Vector3.Zero;
        _from = GlobalTransform.AffineInverse() * ship.GlobalTransform;
        _t = 0;
        State = PadState.Holding;
    }

    private void Let(bool go)
    {
        if (HeldShip != null && IsInstanceValid(HeldShip) && HeldShip.Holder == this)
            HeldShip.Holder = null;
        HeldShip = null;
        State = PadState.Idle;
        ShowGlow();
    }

    private static bool WantsOut(ShipBody ship) => ship.Device<RadioDevice>() is { Launching: true };

    // How far the nose is turned from the pad's heading (right positive), and the up from the pad's up, radians
    private static float Heading(Basis local)
    {
        Vector3 f = -local.Z;
        return Mathf.Atan2(f.X, -f.Z);
    }

    private static float Tilt(Basis local) => local.Y.Normalized().AngleTo(Vector3.Up);

    private static float Smooth(double x)
    {
        float k = (float)Math.Clamp(x, 0, 1);
        return k * k * (3 - 2 * k);
    }

    private static Aabb BoundsOf(Node3D ship)
    {
        if (ship is ShipBody body)
            return body.Bounds;
        ulong id = ship.GetInstanceId();
        if (!s_bounds.TryGetValue(id, out Aabb box))
        {
            Hitbox[] boxes = Hitbox.FromShapes(ship);
            box = boxes.Length > 0 ? Hitbox.Bounds(boxes) : new Aabb(new Vector3(-2.5f, -1.5f, -5), new Vector3(5, 3, 10));
            s_bounds[id] = box;
        }
        return box;
    }

    private void ShowGlow()
    {
        if (State == _shown)
            return;
        _shown = State;
        Color c = State switch
        {
            PadState.Waiting => WaitingColor,
            PadState.Aligned => AlignedColor,
            PadState.Holding => HoldingColor,
            PadState.Docked => DockedColor,
            PadState.Launching => LaunchingColor,
            _ => Colors.Black,
        };
        bool on = State != PadState.Idle;
        foreach (var (mesh, light) in _glows)
        {
            if (mesh != null)
            {
                mesh.Visible = on;
                mesh.SetInstanceShaderParameter("glow_color", c);
            }
            if (light != null)
            {
                light.Visible = on;
                light.LightColor = c;
            }
        }
    }
}
