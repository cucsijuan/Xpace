using Godot;

namespace Xpace.Ship;

/// <summary>
/// A model of gimballed gun turret (a .tres per model, in assets/weapons/turrets).
/// </summary>
[GlobalClass]
public partial class TurretSpec : Resource
{
    /// <summary>The model's name, as a program or a fittings list shows it.</summary>
    [Export] public string Model = "GUN";

    /// <summary>The hardpoint size it needs.</summary>
    [Export] public int Size = 1;

    /// <summary>What it looks like and how it moves: a scene with a <see cref="TurretRig"/> (scenes/weapons/turrets). None: a plain placeholder.</summary>
    [Export] public PackedScene Scene;

    [ExportGroup("Rounds")]
    /// <summary>m/s.</summary>
    [Export] public float MuzzleSpeed = 1200f;
    /// <summary>Seconds between rounds.</summary>
    [Export] public double FireInterval = 0.1;
    [Export] public float Damage = 10f;
    /// <summary>Spread of the rounds (sigma), cold barrel.</summary>
    [Export] public float DispersionMrad = 0.8f;
    [Export] public float RecoilKickMrad = 0.4f;

    [ExportGroup("Servos")]
    [Export] public float ServoDegreesPerSecond = 150f;
    /// <summary>Degrees/s².</summary>
    [Export] public float ServoAcceleration = 600f;
    /// <summary>1/s: how fast it closes an error.</summary>
    [Export] public float ServoGain = 12f;
    /// <summary>Degrees either side.</summary>
    [Export] public float AzimuthLimit = 170f;
    [Export] public float ElevationMin = -5f;
    [Export] public float ElevationMax = 85f;

    [ExportGroup("Heat")]
    [Export] public float HeatPerShot = 5f;
    [Export] public float CoolingPerSecond = 35f;
    /// <summary>It stops above this.</summary>
    [Export] public float HotAbove = 250f;
    /// <summary>And starts again below this.</summary>
    [Export] public float CoolBelow = 120f;

    [ExportGroup("Magazine")]
    /// <summary>The rounds it fires, bought at an arms dealer (none: a test mount that never runs out).</summary>
    [Export] public Xpace.Space.AmmoSpec Ammunition;
    /// <summary>How many its magazine holds.</summary>
    [Export] public int Rounds = 500;
}
