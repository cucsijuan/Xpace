using System;
using System.Collections.Generic;
using Godot;
using Xpace.Computer;
using Xpace.Controls;
using Xpace.Net;

namespace Xpace.Ship;

/// <summary>
/// The ship in flight: a rigid body pushed by its thrusters, and the three
/// devices that connect it to the computer's bus. Every physics step:
///
///   1. the pilot's controls and the inertial unit's sample go into the
///      bus registers, and the inertial unit asks for its interrupt;
///   2. the computer runs for the same step;
///   3. the thruster commands come back out and push the ship.
///
/// The ship moves by its own integrator (<see cref="ShipMotion"/>), not by
/// the physics engine: the body only carries the collision shapes. Setting
/// the node's transform still moves the ship (the floating origin, tests).
///
/// After each step the ship meets the rocks it touches (<see cref="ShipContact"/>):
/// it bounces off them, and <see cref="Impact"/> tells its hull how hard.
///
/// Other devices (radar, turrets, displays) are nodes below the ship that
/// implement <see cref="IShipDevice"/>; they get the same two calls.
///
/// If the program stops sending commands for <see cref="WatchdogSeconds"/>,
/// the thruster controller takes the controls directly (direct mode).
/// See software/flight/flight.asm for the registers.
/// </summary>
public partial class ShipBody : AnimatableBody3D, IControlHost, INetShip
{
    public const int SlotControls = 0, SlotInertial = 1, SlotThrusters = 2, SlotRatings = 10;
    public const byte TypeControls = 1, TypeInertial = 2, TypeThrusters = 3, TypeRatings = 12;

    [Export] public ShipSpec Spec;
    [Export] public NodePath ComputerPath = "ShipComputer";
    [Export] public double WatchdogSeconds = 0.1;
    /// <summary>What goes when it blows up, fading into the fire and smoke (its outside: model, plumes), until it comes back.</summary>
    [Export] public Godot.Collections.Array<NodePath> HideWhenDestroyed = new();

    /// <summary>Who flies the ship; null leaves the controls centred.</summary>
    public Func<PilotControls> ControlsSource;

    public ShipComputer Computer { get; private set; }

    /// <summary>The thrusters follow the controls directly: no program is flying the ship.</summary>
    public bool DirectMode { get; private set; } = true;

    /// <summary>The mode the flight program shows (bit 0: flight assist on).</summary>
    public bool AssistOn => Computer != null && (Computer.Bus[SlotThrusters, 15] & 1) != 0 && !DirectMode;

    /// <summary>The flight program's parking mode is on (bit 2 of the mode shown).</summary>
    public bool ParkingOn => Computer != null && (Computer.Bus[SlotThrusters, 15] & 4) != 0 && !DirectMode;

    /// <summary>Parking, and keeping to a station's speed limit (bit 3): it has the laws and is inside their zone.</summary>
    public bool ParkingLimited => ParkingOn && (Computer.Bus[SlotThrusters, 15] & 8) != 0;

    public PilotControls Controls { get; private set; }

    /// <summary>Metres a second, world axes.</summary>
    public Vector3 LinearVelocity { get; set; }

    /// <summary>Radians a second, world axes.</summary>
    public Vector3 AngularVelocity { get; set; }

    /// <summary>Blown up (its <see cref="ShipHull"/> says so): no computer, no devices, no thrust; it only drifts.</summary>
    public bool Wrecked { get; set; }

    /// <summary>
    /// What holds the ship (a landing pad's clamps), or null: held, the ship
    /// goes where the holder puts it, whatever the thrusters ask; its
    /// computer and devices run on.
    /// </summary>
    public IShipHold Holder { get; set; }

    /// <summary>Held by a landing pad's clamps (see <see cref="Holder"/>).</summary>
    public bool Held => Holder != null;

    /// <summary>The box round the ship's hitboxes, in its own frame (how much room it needs).</summary>
    public Aabb Bounds { get; private set; }

    /// <summary>The ship hit a rock this step: the kinetic energy the crash took (joules).</summary>
    public event Action<float> Impact;

    /// <summary>The last commands, -1..1: pitch, roll, yaw torque; right, up, forward force.</summary>
    public readonly float[] Commands = new float[6];

    private double _sinceCommand = double.MaxValue;
    private readonly List<IShipDevice> _devices = new();
    private PowerDevice _power;
    private EngineDevice _engine;
    private ShipSignature _signature;
    private Quaternion _orientation = Quaternion.Identity;
    private Basis _written = Basis.Identity;
    private Hitbox[] _hitboxes;
    private float _bound;
    private readonly List<Space.RockSphere> _near = new();

    public override void _Ready()
    {
        Spec ??= new ShipSpec();
        SyncToPhysics = false;

        Computer = GetNode<ShipComputer>(ComputerPath);
        Computer.RunsItself = false;
        ShipBus bus = Computer.Bus;
        bus[SlotControls, 0] = TypeControls;
        bus[SlotInertial, 0] = TypeInertial;
        bus[SlotThrusters, 0] = TypeThrusters;
        bus[SlotRatings, 0] = TypeRatings;
        for (int slot = 0; slot < 3; slot++)
            bus[slot, 1] = 1;                       // version
        bus[SlotControls, 1] = 2;                   // +15: more buttons
        bus[SlotRatings, 1] = 2;                    // the whole plate in memory too
        foreach (Node node in FindChildren("*", "", true, false))
            if (node is IShipDevice device)
                _devices.Add(device);
        _power = Device<PowerDevice>();
        _engine = Device<EngineDevice>();
        foreach (Node node in GetChildren())
            _signature ??= node as ShipSignature;

        // What meets rocks: the hitboxes (a plain box if it has none)
        _hitboxes = Hitbox.FromShapes(this);
        if (_hitboxes.Length == 0)
            _hitboxes = new[] { new Hitbox(Vector3.Zero, new Vector3(5, 3, 10)) };
        _bound = ShipContact.Bound(_hitboxes);
        Bounds = Hitbox.Bounds(_hitboxes);
    }

    public T Device<T>() where T : class, IShipDevice
    {
        foreach (IShipDevice d in _devices)
            if (d is T t)
                return t;
        return null;
    }

    private double _downFor;

    public override void _Process(double delta)
    {
        if (HideWhenDestroyed.Count == 0)
            return;
        bool down = Wrecked || _shownDestroyed;
        if (!down && _downFor == 0)
            return;
        _downFor = down ? _downFor + delta : 0;
        float gone = down ? Space.Explosion.Gone(_downFor) : 0;
        foreach (NodePath path in HideWhenDestroyed)
            if (GetNodeOrNull<Node3D>(path) is Node3D part)
                Space.Explosion.Fade(part, gone);
    }

    public override void _PhysicsProcess(double delta)
    {
        ShipBus bus = Computer.Bus;
        Controls = ControlsSource?.Invoke() ?? default;
        if (Wrecked)
        {
            System.Array.Clear(Commands);
            Push((float)delta);
            return;
        }
        WriteControls(bus, Controls);
        WriteInertial(bus);
        WriteRatings(bus);
        foreach (IShipDevice d in _devices)
            d.BeforeStep(this, delta);
        bus.RaiseIrq();

        Computer.Advance(delta);

        foreach (IShipDevice d in _devices)
            d.AfterStep(this, delta);

        if (bus.TakeWritten(SlotThrusters, 14))
            _sinceCommand = 0;
        else
            _sinceCommand += delta;
        DirectMode = _sinceCommand > WatchdogSeconds;
        bus[SlotThrusters, 14] = (byte)(DirectMode ? 1 : 0);

        if (DirectMode)
        {
            PilotControls c = Controls;
            Commands[0] = c.Pitch; Commands[1] = c.Roll; Commands[2] = c.Yaw;
            Commands[3] = c.Right; Commands[4] = c.Up; Commands[5] = c.Throttle;
        }
        else
        {
            for (int i = 0; i < 6; i++)
                Commands[i] = bus.Word(SlotThrusters, 2 + i * 2) / 32767f;
        }
        if (Holder != null)
        {
            System.Array.Clear(Commands);
            State = Holder.Hold(this, delta);
            return;
        }
        if (_engine != null)
        {
            // The main engine's controller decides what it gives of what was asked
            float pushed = _engine.Drive(Spec, this, MathF.Max(0, Commands[5]), delta);
            if (Commands[5] >= 0)
                Commands[5] = pushed / Spec.MainThrust;
        }
        Powered(delta);
        Push((float)delta);
    }

    /// <summary>The ship as the integrator sees it; setting it moves the ship.</summary>
    public ShipState State
    {
        get
        {
            Basis basis = GlobalBasis;
            if (!basis.IsEqualApprox(_written))     // turned from outside
                _orientation = basis.GetRotationQuaternion();
            return new ShipState
            {
                Position = GlobalPosition,
                Orientation = _orientation,
                Velocity = LinearVelocity,
                AngularVelocity = AngularVelocity,
            };
        }
        set
        {
            _orientation = value.Orientation;
            GlobalTransform = new Transform3D(new Basis(value.Orientation), value.Position);
            _written = GlobalBasis;
            LinearVelocity = value.Velocity;
            AngularVelocity = value.AngularVelocity;
        }
    }

    /// <summary>What the main motor pushes, 0 to 1: its plume and its heat (a puppet: what its owner's snapshots say).</summary>
    public float EngineThrust => Wrecked ? 0 : _shownThrust ?? MathF.Max(0, Commands[5]);

    private float? _shownThrust;
    private bool _shownDestroyed;

    // In a session: the main motor's push is its heat and plume to the others
    float INetShip.Thrust => EngineThrust;
    float INetShip.Heat => Wrecked ? 0 : _signature?.HullLevel ?? 0;
    bool INetShip.Exploded => false;

    /// <summary>A puppet (another machine flies it): its motor as its owner says, and its end shown when it blows up.</summary>
    void INetShip.Follow(RemoteTrack.Pose pose)
    {
        _shownThrust = pose.Destroyed ? 0 : pose.Thrust;
        if (_signature != null)
            _signature.ShownLevel = pose.Heat;
        if (pose.Destroyed && !_shownDestroyed)
            Space.Explosion.Spawn(GetParent(), GlobalPosition, 30f, 1.6, 16f);
        _shownDestroyed = pose.Destroyed;
    }

    void INetShip.End(bool exploded) { }

    private static void WriteControls(ShipBus bus, PilotControls c)
    {
        bus.SetWord(SlotControls, 2, Axis(c.Pitch));
        bus.SetWord(SlotControls, 4, Axis(c.Roll));
        bus.SetWord(SlotControls, 6, Axis(c.Yaw));
        bus.SetWord(SlotControls, 8, Axis(c.Right));
        bus.SetWord(SlotControls, 10, Axis(c.Up));
        bus.SetWord(SlotControls, 12, Axis(c.Throttle));
        bus[SlotControls, 14] = (byte)((c.AssistButton ? 1 : 0) | (c.BoostButton ? 2 : 0) |
                                        (c.TargetAhead ? 4 : 0) | (c.NextTarget ? 8 : 0) | (c.Trigger ? 16 : 0) |
                                        (c.NextWeapon ? 32 : 0) | (c.Chaff ? 64 : 0) | (c.Flare ? 128 : 0));
        bus[SlotControls, 15] = (byte)((c.SafeButton ? 1 : 0) | (c.PipSystems ? 2 : 0) | (c.PipEngines ? 4 : 0) |
                                        (c.PipWeapons ? 8 : 0) | (c.PipReset ? 16 : 0) | (c.HeatSink ? 32 : 0) |
                                        (c.Jammer ? 64 : 0) | (c.Parking ? 128 : 0));
    }

    /// <summary>
    /// The thrusters' rating plate (slot 10): what they can do to this ship as
    /// it is, so a program can fly within it. Accelerations in 1/8 m/s²:
    /// +2 forward (the main engine's rating), +4 reverse, +6 sideways, +8 up
    /// and down; turning ones in 1/1024 rad/s²: +10 pitch, +12 yaw, +14 roll.
    ///
    /// Version 2: the whole plate at D000:1400 (<see cref="ShipMemory.Ratings"/>),
    /// 14 words: the same seven accelerations, then where each meets the
    /// veil's drag: +14 forward, +16 reverse, +18 sideways, +20 up and down
    /// top speeds (1/8 m/s), +22 pitch, +24 yaw, +26 roll top turn rates
    /// (1/1024 rad/s); 32767 where there is no top. Better thrusters, a
    /// stronger engine or a lighter ship read higher.
    /// </summary>
    private unsafe void WriteRatings(ShipBus bus)
    {
        ShipSpec s = Spec;
        float forward = _engine?.Spec?.RatedThrust ?? s.MainThrust;
        Vector3 turn = s.TurnAcceleration, top = s.TopTurnRate();
        Span<int> plate = stackalloc int[]
        {
            Rating(forward / s.Mass * 8), Rating(s.Reverse / s.Mass * 8), Rating(s.Lateral / s.Mass * 8), Rating(s.Vertical / s.Mass * 8),
            Rating(turn.X * 1024), Rating(turn.Y * 1024), Rating(turn.Z * 1024),
            Rating(s.TopSpeed(forward) * 8), Rating(s.TopSpeed(s.Reverse) * 8),
            Rating(s.TopSpeed(s.Lateral, s.VeilCross.X) * 8), Rating(s.TopSpeed(s.Vertical, s.VeilCross.Y) * 8),
            Rating(top.X * 1024), Rating(top.Y * 1024), Rating(top.Z * 1024),
        };
        byte* table = Computer.Memory + ShipMemory.Base + ShipMemory.Ratings;
        for (int i = 0; i < plate.Length; i++)
        {
            if (i < 7)
                bus.SetWord(SlotRatings, 2 + i * 2, plate[i]);
            table[i * 2] = (byte)plate[i];
            table[i * 2 + 1] = (byte)(plate[i] >> 8);
        }
    }

    private static int Rating(float x) => float.IsFinite(x) ? (int)Math.Clamp(MathF.Round(x), 0, 32767) : 32767;

    /// <summary>
    /// Turn rates in 1/1024 rad/s and speeds in 1/8 m/s, about and along the
    /// ship's own axes. Godot's ship looks down -Z with +Y up: pitch up is
    /// +X, roll right is -Z, yaw right is -Y.
    /// </summary>
    private void WriteInertial(ShipBus bus)
    {
        Basis toShip = GlobalBasis.Inverse();
        Vector3 w = toShip * AngularVelocity;
        Vector3 v = toShip * LinearVelocity;
        bus.SetWord(SlotInertial, 2, (int)MathF.Round(w.X * 1024));
        bus.SetWord(SlotInertial, 4, (int)MathF.Round(-w.Z * 1024));
        bus.SetWord(SlotInertial, 6, (int)MathF.Round(-w.Y * 1024));
        bus.SetWord(SlotInertial, 8, (int)MathF.Round(v.X * 8));
        bus.SetWord(SlotInertial, 10, (int)MathF.Round(v.Y * 8));
        bus.SetWord(SlotInertial, 12, (int)MathF.Round(-v.Z * 8));
        bus[SlotInertial, 14] = 1;                  // a new sample
    }

    private void Push(float dt)
    {
        ShipSpec s = Spec;
        Vector3 t = s.TurnTorque;
        var torque = new Vector3(Commands[0] * t.X, -Commands[2] * t.Y, -Commands[1] * t.Z);
        float forward = Commands[5] >= 0 ? Commands[5] * s.MainThrust : Commands[5] * s.Reverse;
        var force = new Vector3(Commands[3] * s.Lateral, Commands[4] * s.Vertical, -forward);
        ShipState before = State;
        ShipState after = ShipMotion.Step(before, force, torque, s.Mass, s.Inertia, dt, s.VeilAxes, s.VeilSpin);
        float crash = ShipContact.Collide(ref after, before.Position, _hitboxes, _bound, s, _near);
        State = after;
        if (crash > 0)
            Impact?.Invoke(crash);
    }

    /// <summary>
    /// The thrusters push as hard as the engines capacitor lets them (if the
    /// ship has a <see cref="PowerDevice"/>): <see cref="ShipSpec.ThrustMegawatts"/>
    /// for the main motor at full, half that for each strafe, a quarter for
    /// each turn; short of power, every one of them pushes less alike.
    /// </summary>
    private void Powered(double delta)
    {
        if (_power == null)
            return;
        float c = MathF.Abs(Commands[5]) + 0.5f * (MathF.Abs(Commands[3]) + MathF.Abs(Commands[4])) +
                  0.25f * (MathF.Abs(Commands[0]) + MathF.Abs(Commands[1]) + MathF.Abs(Commands[2]));
        double wanted = Spec.ThrustMegawatts * c * delta;
        if (wanted <= 0)
            return;
        double got = _power.DrawUpTo(PowerStore.Engines, wanted);
        if (got >= wanted)
            return;
        float share = (float)(got / wanted);
        for (int i = 0; i < 6; i++)
            Commands[i] *= share;
    }

    private static int Axis(float x) => (int)MathF.Round(Math.Clamp(x, -1f, 1f) * 32767);
}
