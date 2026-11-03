using Godot;

namespace Xpace.Ship;

/// <summary>
/// A model of pulse laser (a .tres per model, in assets/weapons/lasers), mounted on a ship's hardpoint
/// (<see cref="Hardpoint"/>). The ship's lasers fire in turn, as one (<see cref="LaserDevice"/>). Bought at an arms
/// dealer, it goes to the pilot's locker there, to be fitted.
/// </summary>
[GlobalClass]
public partial class LaserSpec : Xpace.Space.Outfit
{
    public LaserSpec()
    {
        Model = "LSR";
        Price = 1500;
    }

    public override string About => "Pulse laser: fixed along the nose, fires in turn with the ship's other lasers";

    public override string Details => $"Size {Size}, {BoltSpeed:0} m/s bolts, {Damage:0} damage, a pulse every {FireInterval:0.##} s";

    /// <summary>The hardpoint size it needs.</summary>
    [Export] public int Size = 1;

    /// <summary>What it looks like on a hardpoint: a scene facing -Z, with a Marker3D "Muzzle" where pulses leave.</summary>
    [Export] public PackedScene Scene;

    [ExportGroup("Pulse")]
    /// <summary>m/s.</summary>
    [Export] public float BoltSpeed = 5000f;
    /// <summary>Seconds one laser takes to fire again: the ship's lasers fire in turn, so two fire twice as often.</summary>
    [Export] public double FireInterval = 0.2;
    [Export] public float Damage = 12f;
    /// <summary>The optics' spread (sigma).</summary>
    [Export] public float DispersionMrad = 0.2f;

    [ExportGroup("Heat")]
    [Export] public float HeatPerPulse = 6f;
    [Export] public float CoolingPerSecond = 30f;
    /// <summary>It stops above this.</summary>
    [Export] public float HotAbove = 250f;
    /// <summary>And starts again below this.</summary>
    [Export] public float CoolBelow = 120f;
    /// <summary>What a pulse leaves in the ship (MJ).</summary>
    [Export] public float ShipHeatPerPulse = 0.4f;

    [ExportGroup("Power")]
    /// <summary>From the weapons capacitor.</summary>
    [Export] public float MegajoulesPerPulse = 0.7f;

    [ExportGroup("Look")]
    /// <summary>How its bolts look (Projectiles' looks).</summary>
    [Export] public byte RoundKind = 1;
    [Export] public Color FlashColour = new(1f, 0.25f, 0.2f);
}
