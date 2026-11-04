using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Ship;

/// <summary>A pilot's work when nothing else calls for it: it flies the ship this step, and says so (see <see cref="FighterAi"/>).</summary>
public interface IPilotJob
{
    /// <summary>What it is doing, for the tests and the curious.</summary>
    string Doing { get; }

    /// <summary>It is at work with the ship stopped: there is no way ahead to keep clear of rocks.</summary>
    bool HoldsStill { get; }

    /// <summary>Flies this step through <paramref name="pilot"/>; false: nothing to do, the pilot goes on as it would.</summary>
    bool Fly(FighterAi pilot, ref PilotControls controls, double delta);
}

/// <summary>
/// A miner's day, for now: it flies to a rock of the nearest field, stops off it, turns its nose on it and fires its
/// ordinary laser. While the pulses reach the rock it cuts <see cref="TonnesPerCycle"/> of <see cref="Ore"/> every
/// <see cref="CycleSeconds"/>; with its hold full it flies to the nearest station that deals in the ore, hands it over
/// (<see cref="StationMarket.Deliver"/>) and goes back. A stand-in until mining is a loop of its own. Put it under the
/// ship, beside its <see cref="FighterAi"/>, which does the flying.
/// </summary>
public partial class MinerJob : Node, IPilotJob, IOriginShift
{
    /// <summary>What it cuts out of the rock.</summary>
    [Export] public Good Ore;
    [Export] public float TonnesPerCycle = 1f;
    [Export] public double CycleSeconds = 12;
    /// <summary>How far off a rock's surface it stops (m).</summary>
    [Export] public float StandOff = 200f;
    /// <summary>How far from a station it hands the ore over (m).</summary>
    [Export] public float HandOverRange = 2500f;
    [Export] public double HandOverSeconds = 8;
    /// <summary>How far round a point of the field it looks for a rock (m).</summary>
    [Export] public float SearchRadius = 900f;
    [Export] public float LeastRockRadius = 15f;
    /// <summary>Degrees off the rock the nose must be to fire.</summary>
    [Export] public float AimCone = 3f;
    [Export] public int Seed = 7;

    private enum Step { ToRock, Cutting, ToStation, HandingOver }

    public string Doing => _step switch
    {
        Step.ToRock => "flying to a rock",
        Step.Cutting => "cutting",
        Step.ToStation => "taking ore home",
        _ => "handing ore over",
    };

    public bool HoldsStill => _step is Step.Cutting or Step.HandingOver;

    /// <summary>Tonnes in its hold.</summary>
    public float Hold { get; private set; }
    /// <summary>Tonnes handed over since it started.</summary>
    public float Delivered { get; private set; }
    /// <summary>The rock it is working, if any.</summary>
    public RockSphere? Rock { get; private set; }

    private ShipBody _ship;
    private LaserDevice _laser;
    private RandomNumberGenerator _random;
    private Step _step;
    private bool _going;
    private double _cut, _waited;
    private StationMarket _market;

    public override void _Ready()
    {
        _ship = ShipBody.Of(this);
        _random = new RandomNumberGenerator { Seed = (ulong)Seed };
    }

    public void ShiftOrigin(Vector3 offset)
    {
        if (Rock is RockSphere rock)
        {
            rock.Centre -= offset;
            Rock = rock;
        }
    }

    private float Capacity => Math.Max(TonnesPerCycle, _ship.Spec?.CargoCapacity ?? 0);

    public bool Fly(FighterAi pilot, ref PilotControls c, double delta)
    {
        _laser ??= _ship.Device<LaserDevice>();
        switch (_step)
        {
            case Step.ToRock:
                if (!_going)
                {
                    Rock = FindRock();
                    if (Rock is not RockSphere rock)
                        return false;
                    Vector3 away = (_ship.GlobalPosition - rock.Centre).Normalized();
                    pilot.Route.Enqueue(rock.Centre + away * (rock.Radius + StandOff));
                    _going = true;
                    return true;
                }
                _going = false;
                _step = Step.Cutting;
                break;
            case Step.Cutting:
                Cut(pilot, ref c, delta);
                break;
            case Step.ToStation:
                if (!_going)
                {
                    _market = NearestMarket();
                    if (_market?.Radio == null)
                        return false;
                    Vector3 station = _market.Radio.GlobalPosition;
                    pilot.Route.Enqueue(station + (_ship.GlobalPosition - station).Normalized() * HandOverRange);
                    _going = true;
                    return true;
                }
                _going = false;
                _waited = 0;
                _step = Step.HandingOver;
                break;
            case Step.HandingOver:
                c.Throttle = 0;
                _waited += delta;
                if (_waited >= HandOverSeconds)
                {
                    _market?.Deliver(Ore, Hold);
                    Delivered += Hold;
                    Hold = 0;
                    _step = Step.ToRock;
                }
                break;
        }
        return true;
    }

    // Nose on the rock and the trigger held; only what reaches the rock cuts it
    private void Cut(FighterAi pilot, ref PilotControls c, double delta)
    {
        if (Rock is not RockSphere rock)
        {
            _step = Step.ToRock;
            return;
        }
        c.Throttle = 0;
        pilot.AimAt(ref c, rock.Centre);
        Vector3 nose = -_ship.GlobalBasis.Z.Normalized(), to = rock.Centre - _ship.GlobalPosition;
        if (Mathf.RadToDeg(nose.AngleTo(to)) > AimCone)
            return;
        c.Trigger = true;
        bool reaches = Obstacles.Segment(_ship.GetWorld3D(), _ship.GlobalPosition, _ship.GlobalPosition + nose * (to.Length() + rock.Radius), out _, out _);
        if (!reaches || _laser == null || _laser.TooHot || _laser.NoPower)
            return;
        _cut += delta;
        if (_cut < CycleSeconds)
            return;
        _cut = 0;
        Hold = Math.Min(Capacity, Hold + TonnesPerCycle);
        if (Hold >= Capacity)
        {
            Rock = null;
            _step = Step.ToStation;
        }
    }

    // A rock big enough round some point of the nearest field in this sector
    private RockSphere? FindRock()
    {
        World3D world = _ship.GetWorld3D();
        RockField field = RockField.All.Where(f => f.GetWorld3D() == world).OrderBy(f => f.GlobalPosition.DistanceSquaredTo(_ship.GlobalPosition)).FirstOrDefault();
        if (field == null)
            return null;
        var rocks = new List<RockSphere>();
        Vector3 at = field.GlobalPosition + new Vector3(_random.RandfRange(-1, 1), _random.RandfRange(-0.2f, 0.2f), _random.RandfRange(-1, 1)) * SearchRadius;
        RockField.OverlapAll(world, at, SearchRadius, rocks);
        var fit = rocks.Where(r => r.Radius >= LeastRockRadius).ToList();
        return fit.Count == 0 ? null : fit.OrderBy(r => r.Centre.DistanceSquaredTo(_ship.GlobalPosition)).First();
    }

    private StationMarket NearestMarket()
    {
        World3D world = _ship.GetWorld3D();
        return StationRadio.All.Where(r => r.World == world && r.Gate == null).OrderBy(r => r.GlobalPosition.DistanceSquaredTo(_ship.GlobalPosition))
                           .Select(r => StationMarket.Of(r.Id)).FirstOrDefault(m => m?.Deals(Ore) == true);
    }
}
