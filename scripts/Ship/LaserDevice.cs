using System;
using System.Collections.Generic;
using Godot;
using Xpace.Computer;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// The ship's pulse lasers, fixed along its nose, bus slot 9 (190h): the
/// pilot aims them by pointing the ship. Each laser is an emitter, firing
/// along its own -Z: the lasers mounted on the ship's hardpoints
/// (<see cref="Hardpoint"/>), and any <see cref="Marker3D"/> below this
/// node (a ship with no hardpoints and no markers fires from this node).
/// While the program keeps writing the fire register they fire in turn,
/// each ready again <see cref="FireInterval"/> after its pulse: three
/// lasers fire half as often again as two, and heat and drain as much
/// more. With none mounted, the slot is empty. A pulse is a bolt
/// flying at <see cref="BoltSpeed"/> on top of the ship's own velocity: far
/// out it takes long enough to arrive that a target has moved, so the
/// pilot has to lead it. Every pulse heats the
/// emitters; too hot, it stops until they cool. Each pulse takes
/// <see cref="MegajoulesPerPulse"/> from the weapons capacitor
/// (<see cref="PowerDevice"/>, if the ship has one: with it dry the laser
/// waits for the power) and leaves <see cref="ShipHeatPerPulse"/> in the
/// ship (<see cref="HeatDevice"/>).
///
/// The gunsight is the program's: it writes where a pulse fired now
/// would meet the target (the lead point, from the radar's track) and the
/// head-up display draws a circle there, and a pipper where the pulses go
/// at that range. Put the pipper on the circle and fire. The sight is only
/// as good as the program's solution: the radar's noise, its filter's lag
/// and a target that turns after the pulse left are all misses.
///
/// Registers: +0 type 11  +1 version 1  +2 status: bit 0 ready, bit 1 no
/// power, bit 2 too hot  +3 heat (0-255)  +4 write 1: fire this step  +5 pulses fired
/// (counts up)  +6 bolt speed (m/s)  +8 +10 +12 the lead point, right, up,
/// forward (1/4 m, ship axes, from the radar's track origin; written by the
/// program)  +14 write bit 0: show the lead circle this step
/// </summary>
public partial class LaserDevice : Node3D, IShipDevice
{
    public const int Slot = 9;
    public const byte Type = 11;

    /// <summary>The model; none: the first one mounted on a hardpoint.</summary>
    [Export] public LaserSpec Spec;
    private LaserSpec S => Spec ?? _mounted ?? (_default ??= new LaserSpec());
    private LaserSpec _mounted, _default;

    public float BoltSpeed => S.BoltSpeed;
    public double FireInterval => S.FireInterval;
    public float Damage => S.Damage;
    public float DispersionMrad => S.DispersionMrad;
    public float HeatPerPulse => S.HeatPerPulse;
    public float CoolingPerSecond => S.CoolingPerSecond;
    public float HotAbove => S.HotAbove;
    public float CoolBelow => S.CoolBelow;
    public float MegajoulesPerPulse => S.MegajoulesPerPulse;
    public float ShipHeatPerPulse => S.ShipHeatPerPulse;
    public byte RoundKind => S.RoundKind;
    public Color FlashColour => S.FlashColour;

    /// <summary>How many lasers it has.</summary>
    public int Emitters => _emitters.Count;
    public int PulsesFired { get; private set; }
    public float Heat { get; private set; }
    public bool TooHot { get; private set; }
    /// <summary>The weapons capacitor hasn't a pulse's power.</summary>
    public bool NoPower { get; private set; }
    /// <summary>The lead point the program wrote this step (world), or null: no circle.</summary>
    public Vector3? LeadPoint => _lead?.World;
    /// <summary>Where the pulses go at the lead point's range (world): the pipper, while there is a circle.</summary>
    public Vector3? Pipper => _pipper?.World;

    private Spot? _lead, _pipper;

    private readonly List<Node3D> _emitters = new();
    private readonly List<OmniLight3D> _flashes = new();
    private readonly List<double> _cooldowns = new();
    private int _next;
    private bool _registersSet;
    private RadarDevice _radar;
    private readonly RandomNumberGenerator _rng = new() { Seed = 2000 };

    public override void _Ready() => Refit(ShipBody.Of(this));

    /// <summary>Finds its lasers again: the ship's were mounted or taken off.</summary>
    public void Refit(ShipBody ship)
    {
        foreach (OmniLight3D f in _flashes)
            if (IsInstanceValid(f))
                f.QueueFree();
        _flashes.Clear();
        _emitters.Clear();
        _mounted = null;
        foreach (Node child in GetChildren())
            if (child is Marker3D m)
                _emitters.Add(m);
        bool hardpoints = false;
        if (ship != null)
            foreach (Node node in ship.FindChildren("*", "", true, false))
                if (node is Hardpoint h)
                {
                    hardpoints = true;
                    if (h.Emitter != null && h.Mounted is LaserSpec laser)
                    {
                        _emitters.Add(h.Emitter);
                        _mounted ??= laser;
                    }
                }
        if (_emitters.Count == 0 && !hardpoints)
            _emitters.Add(this);
        foreach (Node3D e in _emitters)
        {
            var flash = new OmniLight3D { LightColor = FlashColour, LightEnergy = 0, OmniRange = 6f };
            e.AddChild(flash);
            _flashes.Add(flash);
        }
        _cooldowns.Clear();
        _cooldowns.AddRange(new double[_emitters.Count]);
        _next = 0;
        _registersSet = false;
    }

    public void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (_emitters.Count == 0)
        {
            for (int i = 0; i < ShipBus.SlotSize; i++)
                bus[Slot, i] = 0;
            return;
        }
        if (!_registersSet)
        {
            _registersSet = true;
            bus[Slot, 0] = Type;
            bus[Slot, 1] = 1;
            bus.SetWord(Slot, 6, (int)BoltSpeed);
        }
        bus[Slot, 2] = (byte)((TooHot || NoPower ? 0 : 1) | (NoPower ? 2 : 0) | (TooHot ? 4 : 0));
        bus[Slot, 3] = (byte)Math.Clamp((int)Heat, 0, 255);
        bus[Slot, 5] = (byte)PulsesFired;
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        float dt = (float)delta;
        Heat = MathF.Max(0, Heat - CoolingPerSecond * dt);
        if (TooHot && Heat < CoolBelow)
            TooHot = false;
        for (int i = 0; i < _cooldowns.Count; i++)
            _cooldowns[i] -= delta;
        foreach (OmniLight3D f in _flashes)
            f.LightEnergy = MathF.Max(0, f.LightEnergy - 60 * dt);

        Sight(ship, bus);

        PowerDevice power = ship.Device<PowerDevice>();
        NoPower = power != null && power.Charge[(int)PowerStore.Weapons] < MegajoulesPerPulse;
        bool fire = bus.TakeWritten(Slot, 4) && (bus[Slot, 4] & 1) != 0;
        int ready = Ready();
        if (!fire || ready < 0 || TooHot || NoPower)
            return;
        power?.Draw(PowerStore.Weapons, MegajoulesPerPulse);
        ship.Device<HeatDevice>()?.Add(ShipHeatPerPulse);
        _cooldowns[ready] = FireInterval;
        Heat += HeatPerPulse;
        TooHot = Heat > HotAbove;
        PulsesFired++;
        Node3D emitter = _emitters[ready];
        _flashes[ready].LightEnergy = 5;
        _next = (ready + 1) % _emitters.Count;
        float spread = DispersionMrad / 1000f;
        Basis bore = emitter.GlobalBasis.Orthonormalized();
        Vector3 direction = (-bore.Z + bore.X * _rng.Randfn(0, spread) + bore.Y * _rng.Randfn(0, spread)).Normalized();
        Projectiles.In(this)?.Fire(emitter.GlobalPosition, ship.LinearVelocity + direction * BoltSpeed, Damage, ship, RoundKind);
    }

    // The next laser in turn that is ready to fire, or -1
    private int Ready()
    {
        for (int i = 0; i < _emitters.Count; i++)
        {
            int k = (_next + i) % _emitters.Count;
            if (_cooldowns[k] <= 0)
                return k;
        }
        return -1;
    }

    /// <summary>The lead circle and the pipper, from what the program wrote this step.</summary>
    private void Sight(ShipBody ship, ShipBus bus)
    {
        bool show = bus.TakeWritten(Slot, 14) && (bus[Slot, 14] & 1) != 0;
        bus.TakeWritten(Slot, 8);
        bus.TakeWritten(Slot, 10);
        bus.TakeWritten(Slot, 12);
        if (!show)
        {
            _lead = _pipper = null;
            return;
        }
        _radar ??= ship.Device<RadarDevice>();
        Node3D origin = _radar?.TrackOrigin ?? this;
        var local = new Vector3(bus.Word(Slot, 8), bus.Word(Slot, 10), -bus.Word(Slot, 12)) / 4f;
        Basis axes = ship.GlobalBasis.Orthonormalized();
        Vector3 lead = origin.GlobalPosition + axes * local;
        _lead = Spot.At(this, lead);
        _pipper = Spot.At(this, GlobalPosition - GlobalBasis.Z.Normalized() * GlobalPosition.DistanceTo(lead));
    }
}
