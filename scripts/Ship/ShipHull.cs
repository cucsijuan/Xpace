using Godot;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>Something a new ship comes with full again: missiles, chaff and flares.</summary>
public interface IRearmable
{
    void Rearm();
}

/// <summary>
/// The ship's structure: what the hits it takes wear away (its signature
/// passes on every round and blast), and crashes into rocks
/// (<see cref="CrashJoulesPerPoint"/>). With <see cref="Integrity"/> gone the
/// ship blows up: what is left is a wreck, its systems dead (no computer,
/// no thrusters, nothing on its sensors), drifting as it was going. With
/// insurance a new ship is ready after <see cref="RespawnSeconds"/> where
/// this one started (<see cref="Spawn"/>), full: hull, missiles, chaff and
/// flares. A <see cref="ShieldDevice"/> on the ship takes the hits and
/// the crashes first; heat (<see cref="Burn"/>) gets past it.
/// Put it under a <see cref="ShipBody"/>.
/// </summary>
public partial class ShipHull : Node, IDamageable
{
    [Export] public float Integrity = 300f;
    [Export] public bool Insured = true;
    [Export] public double RespawnSeconds = 6;
    /// <summary>Kinetic energy a crash must take to wear one point of integrity (joules).</summary>
    [Export] public float CrashJoulesPerPoint = 80_000f;

    [Signal] public delegate void DestroyedEventHandler();
    [Signal] public delegate void RestoredEventHandler();

    public float Left { get; private set; }
    public bool Wrecked { get; private set; }
    /// <summary>Seconds until the insurer's new ship (while wrecked and insured).</summary>
    public double RespawnIn { get; private set; }
    public int Losses { get; private set; }
    /// <summary>All the integrity crashes have taken.</summary>
    public float CrashDamage { get; private set; }
    public float Fraction => Left / Integrity;

    /// <summary>Where a new ship appears: where this one was when the game put it there.</summary>
    public Transform3D Spawn { get; set; }

    private ShipBody _ship;
    private ShieldDevice _shield;

    public override void _Ready()
    {
        Left = Integrity;
        _ship = GetParent<ShipBody>();
        Spawn = _ship.GlobalTransform;
        _ship.Impact += Crash;
        foreach (Node n in _ship.GetChildren())
            _shield ??= n as ShieldDevice;
    }

    public override void _ExitTree() => _ship.Impact -= Crash;

    /// <summary>A crash into a rock: what the ship's motion lost is what crushed it.</summary>
    private void Crash(float joules)
    {
        CrashDamage += joules / CrashJoulesPerPoint;
        Hit(joules / CrashJoulesPerPoint);
    }

    public void Hit(float damage)
    {
        if (Wrecked || damage <= 0)
            return;
        Burn(_shield?.Absorb(damage) ?? damage);
    }

    /// <summary>Damage from inside (the ship's own heat): no shield stops it.</summary>
    public void Burn(float damage)
    {
        if (Wrecked || damage <= 0)
            return;
        Left -= damage;
        if (Left <= 0)
            Destroy();
    }

    /// <summary>Blown up: an explosion where it was, and a wreck from now on.</summary>
    public void Destroy()
    {
        if (Wrecked)
            return;
        Left = 0;
        Wrecked = true;
        Losses++;
        RespawnIn = RespawnSeconds;
        _ship.Wrecked = true;
        foreach (Node n in _ship.FindChildren("*", "", true, false))
            (n as RadarDevice)?.Silence();
        Explosion.Spawn(_ship.GetParent(), _ship.GlobalPosition, 30f, 1.6, 16f);
        EmitSignal(SignalName.Destroyed);
    }

    /// <summary>The insurer's new ship, where the first one started, full.</summary>
    public void Restore()
    {
        _ship.State = new ShipState { Position = Spawn.Origin, Orientation = Spawn.Basis.GetRotationQuaternion() };
        Left = Integrity;
        Wrecked = false;
        _ship.Wrecked = false;
        foreach (Node n in _ship.FindChildren("*", "", true, false))
            (n as IRearmable)?.Rearm();
        EmitSignal(SignalName.Restored);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Wrecked || !Insured)
            return;
        RespawnIn -= delta;
        if (RespawnIn <= 0)
            Restore();
    }
}
