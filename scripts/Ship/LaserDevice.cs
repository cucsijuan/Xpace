using System;
using System.Collections.Generic;
using Godot;
using Xpace.Computer;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// A pulse laser fixed along the ship's nose, bus slot 9 (190h): the pilot
/// aims it by pointing the ship. While the program keeps writing the fire
/// register it fires a pulse every <see cref="FireInterval"/>, from its
/// emitters in turn (the <see cref="Marker3D"/> nodes below it, each
/// firing along its own -Z; with none, from itself). A pulse is a bolt
/// flying at <see cref="BoltSpeed"/> on top of the ship's own velocity: far
/// out it takes long enough to arrive that a target has moved, so the
/// pilot has to lead it. Every pulse heats the
/// emitters; too hot, it stops until they cool.
///
/// The gunsight is the program's: it writes where a pulse fired now
/// would meet the target (the lead point, from the radar's track) and the
/// head-up display draws a circle there, and a pipper where the pulses go
/// at that range. Put the pipper on the circle and fire. The sight is only
/// as good as the program's solution: the radar's noise, its filter's lag
/// and a target that turns after the pulse left are all misses.
///
/// Registers: +0 type 11  +1 version 1  +2 status: bit 0 ready, bit 2 too
/// hot  +3 heat (0-255)  +4 write 1: fire this step  +5 pulses fired
/// (counts up)  +6 bolt speed (m/s)  +8 +10 +12 the lead point, right, up,
/// forward (1/4 m, ship axes, from the radar's track origin; written by the
/// program)  +14 write bit 0: show the lead circle this step
/// </summary>
public partial class LaserDevice : Node3D, IShipDevice
{
    public const int Slot = 9;
    public const byte Type = 11;

    [Export] public float BoltSpeed = 2000f;            // m/s
    [Export] public double FireInterval = 0.15;
    [Export] public float Damage = 12f;
    [Export] public float DispersionMrad = 0.2f;        // the optics' spread (sigma)
    [Export] public float HeatPerPulse = 9f, CoolingPerSecond = 30f;
    [Export] public float HotAbove = 250f, CoolBelow = 120f;
    [Export] public byte RoundKind = 1;                 // how its bolts look (Projectiles' looks)
    [Export] public Color FlashColour = new(1f, 0.25f, 0.2f);

    public int PulsesFired { get; private set; }
    public float Heat { get; private set; }
    public bool TooHot { get; private set; }
    /// <summary>The lead point the program wrote this step (world), or null: no circle.</summary>
    public Vector3? LeadPoint { get; private set; }
    /// <summary>Where the pulses go at the lead point's range (world): the pipper, while there is a circle.</summary>
    public Vector3? Pipper { get; private set; }

    private readonly List<Node3D> _emitters = new();
    private readonly List<OmniLight3D> _flashes = new();
    private int _next;
    private double _cooldown;
    private bool _registersSet;
    private RadarDevice _radar;
    private readonly RandomNumberGenerator _rng = new() { Seed = 2000 };

    public override void _Ready()
    {
        foreach (Node child in GetChildren())
            if (child is Marker3D m)
                _emitters.Add(m);
        if (_emitters.Count == 0)
            _emitters.Add(this);
        foreach (Node3D e in _emitters)
        {
            var flash = new OmniLight3D { LightColor = FlashColour, LightEnergy = 0, OmniRange = 6f };
            e.AddChild(flash);
            _flashes.Add(flash);
        }
    }

    public void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (!_registersSet)
        {
            _registersSet = true;
            bus[Slot, 0] = Type;
            bus[Slot, 1] = 1;
            bus.SetWord(Slot, 6, (int)BoltSpeed);
        }
        bus[Slot, 2] = (byte)((TooHot ? 0 : 1) | (TooHot ? 4 : 0));
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
        _cooldown -= delta;
        foreach (OmniLight3D f in _flashes)
            f.LightEnergy = MathF.Max(0, f.LightEnergy - 60 * dt);

        Sight(ship, bus);

        bool fire = bus.TakeWritten(Slot, 4) && (bus[Slot, 4] & 1) != 0;
        if (!fire || _cooldown > 0 || TooHot)
            return;
        _cooldown = FireInterval;
        Heat += HeatPerPulse;
        TooHot = Heat > HotAbove;
        PulsesFired++;
        Node3D emitter = _emitters[_next];
        _flashes[_next].LightEnergy = 5;
        _next = (_next + 1) % _emitters.Count;
        float spread = DispersionMrad / 1000f;
        Basis bore = emitter.GlobalBasis.Orthonormalized();
        Vector3 direction = (-bore.Z + bore.X * _rng.Randfn(0, spread) + bore.Y * _rng.Randfn(0, spread)).Normalized();
        Projectiles.Instance?.Fire(emitter.GlobalPosition, ship.LinearVelocity + direction * BoltSpeed, Damage, ship, RoundKind);
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
            LeadPoint = Pipper = null;
            return;
        }
        _radar ??= ship.GetNodeOrNull<RadarDevice>("Radar");
        Node3D origin = _radar?.TrackOrigin ?? this;
        var local = new Vector3(bus.Word(Slot, 8), bus.Word(Slot, 10), -bus.Word(Slot, 12)) / 4f;
        Basis axes = ship.GlobalBasis.Orthonormalized();
        Vector3 lead = origin.GlobalPosition + axes * local;
        LeadPoint = lead;
        Pipper = GlobalPosition - GlobalBasis.Z.Normalized() * GlobalPosition.DistanceTo(lead);
    }
}
