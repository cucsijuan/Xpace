using Godot;

namespace Xpace.Space;

/// <summary>
/// A model of missile (a .tres per model, in assets/weapons/missiles): what it can do. What it decides to do is its firmware's.
/// </summary>
[GlobalClass]
public partial class MissileSpec : Resource
{
    /// <summary>The model's name, as a program or a fittings list shows it.</summary>
    [Export] public string Model = "MSL";

    [ExportGroup("Motor")]
    /// <summary>m/s the fuel gives in all.</summary>
    [Export] public float DeltaV = 1500f;

    [ExportGroup("Airframe")]
    /// <summary>The hardest it can push, m/s², across its flight as well as along it.</summary>
    [Export] public float MaxAcceleration = 400f;
    /// <summary>Seconds before its warhead is armed.</summary>
    [Export] public float ArmSeconds = 0.5f;
    /// <summary>Seconds it flies before it gives up.</summary>
    [Export] public float LifeSeconds = 30f;

    [ExportGroup("Warhead")]
    /// <summary>The proximity fuse goes off this near a target (m).</summary>
    [Export] public float FuseRadius = 25f;
    /// <summary>Within this the fragments do all their damage (m).</summary>
    [Export] public float LethalRadius = 15f;
    /// <summary>Past this they do none (m); between, they thin with the square of the distance.</summary>
    [Export] public float BlastRadius = 50f;
    /// <summary>What it does within the lethal radius.</summary>
    [Export] public float Damage = 250f;

    [ExportGroup("Firmware")]
    /// <summary>How fast its microcontroller runs.</summary>
    [Export] public uint InstructionsPerSecond = 400_000;
    /// <summary>The program in its ROM.</summary>
    [Export(PropertyHint.File, "*.bin")] public string FirmwarePath = "res://disks/missile.bin";

    [ExportGroup("Heat seeker")]
    /// <summary>It guides itself by a seeker head, not by the launcher's datalink.</summary>
    [Export] public bool HeatSeeker = false;
    /// <summary>The whole cone the head looks at.</summary>
    [Export] public float SeekerFieldDegrees = 8f;
    /// <summary>The least heat it notices.</summary>
    [Export] public float SeekerSensitivity = 1e-6f;
    /// <summary>How fast the gimbal turns, per second.</summary>
    [Export] public float SeekerSlewDegrees = 60f;
    /// <summary>How far off the nose it can look.</summary>
    [Export] public float GimbalLimitDegrees = 50f;
}
