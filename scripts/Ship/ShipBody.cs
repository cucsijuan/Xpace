using System;
using System.Collections.Generic;
using Godot;
using Xpace.Computer;
using Xpace.Controls;

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
/// Other devices (radar, turrets, displays) are nodes below the ship that
/// implement <see cref="IShipDevice"/>; they get the same two calls.
///
/// If the program stops sending commands for <see cref="WatchdogSeconds"/>,
/// the thruster controller takes the controls directly (direct mode).
/// See software/flight/flight.asm for the registers.
/// </summary>
public partial class ShipBody : AnimatableBody3D, IControlHost
{
    public const int SlotControls = 0, SlotInertial = 1, SlotThrusters = 2;
    public const byte TypeControls = 1, TypeInertial = 2, TypeThrusters = 3;

    [Export] public ShipSpec Spec;
    [Export] public NodePath ComputerPath = "ShipComputer";
    [Export] public double WatchdogSeconds = 0.1;

    /// <summary>Who flies the ship; null leaves the controls centred.</summary>
    public Func<PilotControls> ControlsSource;

    public ShipComputer Computer { get; private set; }

    /// <summary>The thrusters follow the controls directly: no program is flying the ship.</summary>
    public bool DirectMode { get; private set; } = true;

    /// <summary>The mode the flight program shows (bit 0: flight assist on).</summary>
    public bool AssistOn => Computer != null && (Computer.Bus[SlotThrusters, 15] & 1) != 0 && !DirectMode;

    public PilotControls Controls { get; private set; }

    /// <summary>Metres a second, world axes.</summary>
    public Vector3 LinearVelocity { get; set; }

    /// <summary>Radians a second, world axes.</summary>
    public Vector3 AngularVelocity { get; set; }

    /// <summary>The last commands, -1..1: pitch, roll, yaw torque; right, up, forward force.</summary>
    public readonly float[] Commands = new float[6];

    private double _sinceCommand = double.MaxValue;
    private readonly List<IShipDevice> _devices = new();
    private Quaternion _orientation = Quaternion.Identity;
    private Basis _written = Basis.Identity;

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
        for (int slot = 0; slot < 3; slot++)
            bus[slot, 1] = 1;                       // version
        foreach (Node node in FindChildren("*", "", true, false))
            if (node is IShipDevice device)
                _devices.Add(device);
    }

    public T Device<T>() where T : class, IShipDevice
    {
        foreach (IShipDevice d in _devices)
            if (d is T t)
                return t;
        return null;
    }

    public override void _PhysicsProcess(double delta)
    {
        ShipBus bus = Computer.Bus;
        Controls = ControlsSource?.Invoke() ?? default;
        WriteControls(bus, Controls);
        WriteInertial(bus);
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
                                        (c.NextWeapon ? 32 : 0));
    }

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
        var torque = new Vector3(
            Commands[0] * s.PitchAcceleration * s.Inertia.X,
            -Commands[2] * s.YawAcceleration * s.Inertia.Y,
            -Commands[1] * s.RollAcceleration * s.Inertia.Z);
        float forward = Commands[5] >= 0 ? Commands[5] * s.MainThrust : Commands[5] * s.ReverseThrust;
        var force = new Vector3(Commands[3] * s.LateralThrust, Commands[4] * s.VerticalThrust, -forward);
        State = ShipMotion.Step(State, force, torque, s.Mass, s.Inertia, dt);
    }

    private static int Axis(float x) => (int)MathF.Round(Math.Clamp(x, -1f, 1f) * 32767);
}
