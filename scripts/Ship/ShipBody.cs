using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
public partial class ShipBody : AnimatableBody3D, IControlHost, INetShip, Space.ICruiseShown, ISolidShip
{
    public const int SlotControls = 0, SlotInertial = 1, SlotThrusters = 2, SlotRatings = 10;
    public const byte TypeControls = 1, TypeInertial = 2, TypeThrusters = 3, TypeRatings = 12;

    [Export]
    public ShipSpec Spec
    {
        get => _spec;
        set
        {
            if (_spec == value)
                return;
            _spec = value;
            SpecChanged?.Invoke();
        }
    }
    private ShipSpec _spec;

    /// <summary>The hull changed (a courtesy ship for an impounded one, say).</summary>
    public event System.Action SpecChanged;

    /// <summary>What the hold carries (kg; <see cref="CargoHold"/> sets it).</summary>
    public float CargoMass { get; set; }

    /// <summary>The hull and what it carries (kg).</summary>
    public float Mass => Spec.Mass + CargoMass;

    [Export] public NodePath ComputerPath = "ShipComputer";

    /// <summary>
    /// What this ship has on its hardpoints in place of its hull's factory fitting (<see cref="ShipSpec.Fitting"/>),
    /// by hardpoint name: a raider with lasers only. A hardpoint not listed is empty; none listed: the factory's.
    /// A pilot's record changes it again (<see cref="PilotShip"/>).
    /// </summary>
    [Export] public Godot.Collections.Dictionary<string, Resource> Loadout = new();
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
    private readonly List<Hardpoint> _hardpoints = new();
    private PowerDevice _power;
    private EngineDevice _engine;
    private CruiseDevice _cruise;
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

        Computer = GetNodeOrNull<ShipComputer>(ComputerPath) ?? FindChildren("*", "", true, false).OfType<ShipComputer>().First();
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
            if (node is Hardpoint h)
                _hardpoints.Add(h);
        Fit(Loadout.Count > 0 ? new Dictionary<string, Resource>(Loadout) : Fittings.Of(Spec));     // a pilot's record may change it (PilotShip)
        foreach (Node node in GetChildren())
            _signature ??= node as ShipSignature;

        // What meets rocks: the hitboxes (a plain box if it has none)
        _hitboxes = Hitbox.FromShapes(this);
        if (_hitboxes.Length == 0)
            _hitboxes = new[] { new Hitbox(Vector3.Zero, new Vector3(5, 3, 10)) };
        _bound = ShipContact.Bound(_hitboxes);
        Bounds = Hitbox.Bounds(_hitboxes);
        ShipCollision.Measure(_bound);
        AddToGroup(ShipCollision.Group);
    }

    private readonly List<Node> _ships = new();

    public Hitbox[] SolidBoxes => _hitboxes;
    public float SolidReach => _bound;
    public Vector3 SolidVelocity => LinearVelocity;
    public float SolidMass => Mass;
    public bool Solid => Visible;
    public bool Immovable => Held;
    public bool MovedHere => !Net.NetBody.IsPuppet(this);

    /// <summary>Another ship ran into this one.</summary>
    public void Rammed(Vector3 push, Vector3 speedChange, float joules)
    {
        if (!MovedHere)
            return;                                     // its owner works it out, there
        if (!Held)
        {
            ShipState now = State;
            now.Position += push;
            now.Velocity += speedChange;
            State = now;
        }
        if (joules > 0)
            Impact?.Invoke(joules);
    }

    /// <summary>The ship <paramref name="node"/> is part of (a device on a hardpoint is not its child), or null.</summary>
    public static ShipBody Of(Node node)
    {
        for (Node n = node?.GetParent(); n != null; n = n.GetParent())
            if (n is ShipBody ship)
                return ship;
        return null;
    }

    /// <summary>
    /// The first <typeparamref name="T"/> on the ship <paramref name="node"/> is part of, wherever it sits in it (the
    /// ship's nodes are grouped in folders); on no ship, the first under the node's parent.
    /// </summary>
    public static T Part<T>(Node node) where T : class
    {
        Node root = Of(node) ?? node?.GetParent();
        return root?.FindChildren("*", "", true, false).OfType<T>().FirstOrDefault();
    }

    /// <summary>Its first <typeparamref name="T"/>, wherever it sits in the ship (ready or not).</summary>
    public T Find<T>() where T : class => FindChildren("*", "", true, false).OfType<T>().FirstOrDefault();

    /// <summary>Its hardpoints (<see cref="Hardpoint"/>), found when it is ready.</summary>
    public IReadOnlyList<Hardpoint> Hardpoints => _hardpoints;

    /// <summary>What is mounted on its hardpoints, as a key to compare (resource paths by hardpoint).</summary>
    public string Fitted { get; private set; } = "";

    /// <summary>
    /// Mounts <paramref name="fitted"/> on its hardpoints (by name; one not listed is left empty, a model it doesn't take
    /// is not mounted, nor a second turret) and finds its devices again. A device taken off leaves its place on the bus
    /// empty.
    /// </summary>
    public void Fit(IReadOnlyDictionary<string, Resource> fitted)
    {
        bool hadTurret = Device<TurretDevice>() != null;
        bool turret = false;
        foreach (Hardpoint h in _hardpoints)
        {
            Resource model = fitted.GetValueOrDefault(h.Name.ToString());
            if (model is TurretSpec && (turret || !h.Takes(model)))
                model = null;
            turret |= model is TurretSpec;
            h.Mount(model);
        }
        Fitted = Key(fitted);
        FindDevices();
        if (hadTurret && Device<TurretDevice>() == null)
            for (int i = 0; i < ShipBus.SlotSize; i++)
                Computer.Bus[TurretDevice.Slot, i] = 0;
    }

    /// <summary>A fitting as a key to compare with <see cref="Fitted"/>.</summary>
    public static string Key(IReadOnlyDictionary<string, Resource> fitted) =>
        string.Join(";", fitted.Where(f => f.Value != null).OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value.ResourcePath}"));

    private void FindDevices()
    {
        _devices.Clear();
        foreach (Node node in FindChildren("*", "", true, false))
            if (node is IShipDevice device)
                _devices.Add(device);
        _power = Device<PowerDevice>();
        _engine = Device<EngineDevice>();
        _cruise = Device<CruiseDevice>();
        Device<LaserDevice>()?.Refit(this);
        Device<LauncherDevice>()?.Refit(this);
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

    /// <summary>
    /// Detail level 1: nobody is dealing with this ship, so its computer and devices rest and its holder alone says
    /// where it is. It is still there for radar and rounds. <see cref="Wake"/> takes it to level 0.
    /// </summary>
    public bool Dormant { get; private set; }

    /// <summary>To detail level 1: only a held ship can do without its computer.</summary>
    public void Rest()
    {
        if (Holder == null || Dormant)
            return;
        Dormant = true;
        Computer.PowerOnAtStart = false;
        Device<RadarDevice>()?.Silence();
        foreach (FighterAi pilot in FindChildren("*", "", true, false).OfType<FighterAi>())
            pilot.SetPhysicsProcess(false);
        foreach (Space.RadarEmitter emitter in FindChildren("*", "", true, false).OfType<Space.RadarEmitter>())
            emitter.SetPhysicsProcess(false);
    }

    /// <summary>To detail level 0: its computer runs, as any ship's.</summary>
    public void Wake()
    {
        if (!Dormant)
            return;
        Dormant = false;
        Computer.PowerOn();
        foreach (FighterAi pilot in FindChildren("*", "", true, false).OfType<FighterAi>())
            pilot.SetPhysicsProcess(true);
        foreach (Space.RadarEmitter emitter in FindChildren("*", "", true, false).OfType<Space.RadarEmitter>())
            emitter.SetPhysicsProcess(true);
    }

    // A ship's step has three stages, and all the ships go through each together: everyone gets ready, every computer runs
    // at the same time on the processor's cores (nothing in a machine touches anything outside it), and everyone acts on
    // what its computer said. The first ship the tree reaches in a physics step runs them for all.
    private static readonly List<ShipBody> InSpace = new();
    private static readonly List<ShipBody> Stepping = new();
    private static readonly List<ShipBody> Computing = new();
    private static ulong _steppedAll;
    private static double _stepDelta;
    private ulong _stepped;

    /// <summary>Who flies it, if a free person does (<see cref="Xpace.World.Universe.ActorOf"/>).</summary>
    public string Actor = "";

    /// <summary>The ship on the universe's records it shows, if any: what it uses up is counted there.</summary>
    public string RecordId = "";

    /// <summary>The station whose stores it draws on (a police ship's), if any.</summary>
    public string Base = "";
    private bool _computing;

    public override void _EnterTree() => InSpace.Add(this);

    public override void _ExitTree() => InSpace.Remove(this);

    public override void _PhysicsProcess(double delta)
    {
        ulong step = Space.Sectors.Step;
        if (_steppedAll != step)
        {
            _steppedAll = step;
            StepAll(step, delta);
        }
        if (_stepped == step)
            return;
        _stepped = step;                                // it came too late for the others' turn
        GetReady(delta);
        if (_computing)
            Computer.Advance(delta);
        Act(delta);
    }

    private static void StepAll(ulong step, double delta)
    {
        Stepping.Clear();
        Computing.Clear();
        foreach (ShipBody ship in InSpace)
            if (ship.IsPhysicsProcessing() && ship.CanProcess())
            {
                ship._stepped = step;
                Stepping.Add(ship);
            }
        foreach (ShipBody ship in Stepping)
            if (IsInstanceValid(ship))
            {
                ship.GetReady(delta);
                if (ship._computing)
                    Computing.Add(ship);
            }
        _stepDelta = delta;
        if (Computing.Count > 1)
            Parallel.For(0, Computing.Count, RunComputer);
        else if (Computing.Count == 1)
            RunComputer(0);
        foreach (ShipBody ship in Stepping)
            if (IsInstanceValid(ship))
                ship.Act(delta);
    }

    private static readonly Action<int> RunComputer = i => Computing[i].Computer.Advance(_stepDelta);

    /// <summary>Before its computer runs: sensors and controls on the bus, the interrupt raised.</summary>
    private void GetReady(double delta)
    {
        _computing = false;
        if (Dormant)
        {
            if (Holder != null)
                State = Holder.Hold(this, delta);
            return;
        }
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
        _computing = true;
    }

    /// <summary>After its computer ran: the devices and the thrusters do what it said.</summary>
    private void Act(double delta)
    {
        if (!_computing)
            return;
        _computing = false;
        ShipBus bus = Computer.Bus;
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
        if (_cruise?.Pushes == true)
        {
            _engine?.Drive(Spec, this, 0, delta);       // the generator pushes: the main engine rests
        }
        else if (_engine != null)
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

    /// <summary>How much of the medium it stirs in cruise, 0 to 1 (a puppet: what its owner's snapshots say).</summary>
    public float WakeLevel => Wrecked ? 0 : _shownWake ?? _cruise?.Wake ?? 0;

    /// <summary>What its holder makes it show: how hard its motor pushes, and how much of the medium it stirs.</summary>
    public void Show(float thrust, float wake)
    {
        _shownThrust = thrust;
        _shownWake = wake;
        if (_signature != null)
            _signature.ShownWake = wake;
    }

    /// <summary>Its holder lets it go: what it shows is its own again.</summary>
    public void Unshow()
    {
        _shownThrust = null;
        _shownWake = null;
        if (_signature != null)
            _signature.ShownWake = 0;
    }

    private float? _shownWake;
    private float? _shownThrust;
    private bool _shownDestroyed;

    // In a session: the main motor's push is its heat and plume to the others
    float INetShip.Thrust => EngineThrust;
    float INetShip.Wake => WakeLevel;
    float INetShip.Heat => Wrecked ? 0 : _signature?.HullLevel ?? 0;
    bool INetShip.Exploded => false;

    /// <summary>A puppet (another machine flies it): its motor as its owner says, and its end shown when it blows up.</summary>
    void INetShip.Follow(RemoteTrack.Pose pose)
    {
        _shownThrust = pose.Destroyed ? 0 : pose.Thrust;
        if (_signature != null)
        {
            _signature.ShownLevel = pose.Heat;
            _signature.ShownWake = pose.Wake;
        }
        _shownWake = pose.Wake;
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
        float forward = ForwardPush;
        Vector3 turn = s.TurnAcceleration, top = s.TopTurnRate();
        float thin = Thin;
        Span<int> plate = stackalloc int[]
        {
            Rating(forward / s.Mass * 8), Rating(s.Reverse / s.Mass * 8), Rating(s.Lateral / s.Mass * 8), Rating(s.Vertical / s.Mass * 8),
            Rating(turn.X * 1024), Rating(turn.Y * 1024), Rating(turn.Z * 1024),
            Rating(s.TopSpeed(forward, s.VeilDrag * thin) * 8), Rating(s.TopSpeed(s.Reverse, s.VeilDrag * thin) * 8),
            Rating(s.TopSpeed(s.Lateral, s.VeilCross.X * thin) * 8), Rating(s.TopSpeed(s.Vertical, s.VeilCross.Y * thin) * 8),
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

    // What pushes it forward now: the cruise generator while it is on, else the main engine at its rating
    private float ForwardPush => _cruise?.Pushes == true ? _cruise.Push : _engine?.Spec?.RatedThrust ?? Spec.MainThrust;

    /// <summary>How fast it can go forward now, as its rating plate says: in cruise, on the thinned veil.</summary>
    public float TopSpeedNow => Spec.TopSpeed(ForwardPush, Spec.VeilDrag * Thin);

    /// <summary>What the veil's drag is multiplied by now (the cruise generator thins it; a wreck's is as normal).</summary>
    private float Thin => Wrecked ? 1 : _cruise?.Thin ?? 1;

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
        bus.SetWord(SlotInertial, 8, Speed(v.X));
        bus.SetWord(SlotInertial, 10, Speed(v.Y));
        bus.SetWord(SlotInertial, 12, Speed(-v.Z));
        bus[SlotInertial, 14] = 1;                  // a new sample
    }

    // The sensor's word: past 4,095 m/s it reads its most
    private static int Speed(float v) => (int)Math.Clamp(MathF.Round(v * 8), -32767, 32767);

    private void Push(float dt)
    {
        ShipSpec s = Spec;
        Vector3 t = s.TurnTorque;
        var torque = new Vector3(Commands[0] * t.X, -Commands[2] * t.Y, -Commands[1] * t.Z);
        float main = _cruise?.Pushes == true && !Wrecked ? _cruise.Push : s.MainThrust;
        float forward = Commands[5] >= 0 ? Commands[5] * main : Commands[5] * s.Reverse;
        var force = new Vector3(Commands[3] * s.Lateral, Commands[4] * s.Vertical, -forward);
        ShipState before = State;
        // Cargo is spread through the hull: it weighs on turning as on going
        float load = Mass / s.Mass;
        ShipState after = ShipMotion.Step(before, force, torque, Mass, s.Inertia * load, dt, s.VeilAxes * Thin, s.VeilSpin);
        float crash = ShipContact.Collide(GetWorld3D(), ref after, before.Position, _hitboxes, _bound, s, _near, load);
        crash += ShipCollision.Meet(this, ref after, before.Position, dt, _ships);
        State = after;
        if (crash > 0)
            Impact?.Invoke(crash);
    }

    /// <summary>
    /// The thrusters push as hard as the engines capacitor lets them (if the
    /// ship has a <see cref="PowerDevice"/>): <see cref="ShipSpec.ThrustMegawatts"/>
    /// for the main motor at full (in cruise, what the generator draws), half
    /// that for each strafe, a quarter for each turn; short of power, every one
    /// of them pushes less alike.
    /// </summary>
    private void Powered(double delta)
    {
        if (_power == null)
            return;
        float c = 0.5f * (MathF.Abs(Commands[3]) + MathF.Abs(Commands[4])) +
                  0.25f * (MathF.Abs(Commands[0]) + MathF.Abs(Commands[1]) + MathF.Abs(Commands[2]));
        float main = MathF.Abs(Commands[5]) * (_cruise?.Pushes == true && Commands[5] > 0 ? _cruise.Megawatts : Spec.ThrustMegawatts);
        double wanted = (Spec.ThrustMegawatts * c + main) * delta;
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
