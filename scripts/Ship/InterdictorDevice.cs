using System;
using Godot;
using Xpace.Computer;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// The interdictor, bus slot 19 (230h): a beam that thickens the medium round
/// the ship the radar tracks, by (Reach / distance)² (GDD 4.3.1). A cruising
/// ship caught in it can thin its veil less and less, slows, and drops out when
/// the density reaches what its generator can bear: a better generator holds
/// on longer, from nearer. It does nothing to a ship not in cruise, and needs
/// the radar's lock. It draws on the systems capacitor, and with too little
/// power its reach falls. In a session the ship's owner is told.
///
/// Registers: +0 type 21  +1 version 1  +2 status: bit 0 on, bit 1 its beam
/// is on a target  +3 the share of the power it wants that it gets, 0 to 255
/// +5 write: 1 on, 0 off  +7 the pilot's interdictor button
/// </summary>
public partial class InterdictorDevice : Node, IShipDevice, IRearmable
{
    public const int Slot = 19;
    public const byte Type = 21, Version = 1;
    private const double TellEvery = 0.2;

    /// <summary>Where the density it adds round its target is 1 (m).</summary>
    [Export] public float Reach = 46000f;
    /// <summary>What it draws while on (MW).</summary>
    [Export] public float Megawatts = 8f;

    public bool On { get; private set; }
    /// <summary>Its beam reached a ship this step.</summary>
    public bool OnTarget { get; private set; }
    /// <summary>A ship to hold the beam on, whatever the radar says (a test).</summary>
    public Node3D Forced;

    private RadarDevice _radar;
    private PowerDevice _power;
    private float _share = 1;
    private double _sinceTold;

    public void Rearm() => On = false;

    public void BeforeStep(ShipBody ship, double delta)
    {
        _radar ??= ship.Device<RadarDevice>();
        _power ??= ship.Device<PowerDevice>();
        OnTarget = false;
        _share = 1;
        if (On)
        {
            if (_power != null)
            {
                double wanted = Megawatts * delta;
                _share = wanted > 0 ? (float)(_power.DrawUpTo(PowerStore.Systems, wanted) / wanted) : 1;
            }
            Reaching(ship, delta);
        }
        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = Version;
        bus[Slot, 2] = (byte)((On ? 1 : 0) | (OnTarget ? 2 : 0));
        bus[Slot, 3] = (byte)Math.Clamp(_share * 255, 0, 255);
        bus[Slot, 7] = (byte)(ship.Controls.Interdict ? 1 : 0);
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (bus.TakeWritten(Slot, 5))
            On = bus[Slot, 5] != 0;
    }

    private void Reaching(ShipBody ship, double delta)
    {
        Node3D target = Forced ?? Combat.ShipOf(_radar?.Tracked);
        if (target == null || !IsInstanceValid(target) || target == ship)
            return;
        OnTarget = true;
        float reach = Reach * _share;
        float density = reach * reach / Mathf.Max(ship.GlobalPosition.DistanceSquaredTo(target.GlobalPosition), 1);
        if (target is ShipBody local && !NetBody.IsPuppet(local))
        {
            local.Device<CruiseDevice>()?.Thicken(density);
            return;
        }
        _sinceTold += delta;
        if (_sinceTold >= TellEvery && SessionNode.Instance is { Active: true } session && session.IdOf(target) is ushort id)
        {
            _sinceTold = 0;
            session.Session.Thicken(id, density);
        }
    }
}
