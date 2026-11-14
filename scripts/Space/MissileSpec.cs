using Godot;

namespace Xpace.Space;

/// <summary>
/// A model of missile (a .tres per model, in assets/weapons/missiles): what it can do. What it decides to do is its firmware's.
/// </summary>
[GlobalClass]
public partial class MissileSpec : Outfit
{
    public MissileSpec()
    {
        Model = "MSL";
        Price = 800;
    }

    /// <summary>What it looks like on a pylon's rail before it is fired: a scene centred on the rail, nose along -Z.</summary>
    [Export] public PackedScene Look;

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

    [ExportGroup("Calibration")]
    // Burned into the firmware's ROM by the factory: the same program, each model's own numbers
    /// <summary>The navigation constant N: how hard it turns for the line of sight's turn.</summary>
    [Export] public float NavigationGain = 3f;
    /// <summary>What it pushes along the way while it can spare it (m/s²), to get there sooner.</summary>
    [Export] public float BoostAcceleration = 150f;
    /// <summary>Radar guided: it boosts while closing slower than this (m/s).</summary>
    [Export] public float CloseSpeed = 700f;
    /// <summary>Heat seeker: the closing speed its turn gain is reckoned for (m/s): its head measures no range.</summary>
    [Export] public float AssumedClosing = 600f;
    /// <summary>Heat seeker: the fuel it keeps for the end and doesn't boost with (m/s).</summary>
    [Export] public float FuelReserve = 400f;
    /// <summary>Seconds without word of its target (the datalink, or heat in its head) before it gives up.</summary>
    [Export] public float LostSeconds = 2f;

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
    /// <summary>How its head tells the target from what else glows: one band, two colours, or a picture.</summary>
    [Export] public SeekerKind SeekerKind = SeekerKind.Sum;
    /// <summary>Two-colour: how far off the learned colour (ln of short band over long) still counts as the target.</summary>
    [Export] public float ColourTolerance = 0.3f;
    /// <summary>Imaging: the finest separation it sees as two things (degrees).</summary>
    [Export] public float ResolutionDegrees = 0.5f;

    public override string About => !HeatSeeker ? "Radar guided: needs your radar's lock all the way" : SeekerKind switch
    {
        SeekerKind.TwoColour => "Heat seeker, two-colour head: guides itself, tells a flare's colour from a motor's",
        SeekerKind.Imaging => "Heat seeker, imaging head: guides itself, keeps to its target as a flare falls away",
        _ => "Heat seeker: guides itself, needs no lock",
    };

    public override string Details => $"Warhead {Damage:0}   Turns at {MaxAcceleration:0} m/s²   Fuel {DeltaV:0} m/s";
}
