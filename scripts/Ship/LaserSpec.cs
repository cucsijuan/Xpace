using Godot;

namespace Xpace.Ship;

/// <summary>
/// A model of pulse laser (a .tres per model, in assets/weapons/lasers).
/// </summary>
[GlobalClass]
public partial class LaserSpec : Resource
{
    /// <summary>The model's name, as a program or a fittings list shows it.</summary>
    [Export] public string Model = "LSR";

    [ExportGroup("Pulse")]
    /// <summary>m/s.</summary>
    [Export] public float BoltSpeed = 5000f;
    /// <summary>Seconds between pulses.</summary>
    [Export] public double FireInterval = 0.1;
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
