using Godot;

namespace Xpace.Ship;

/// <summary>What a hull can do: its mass and the most its thrusters give.</summary>
[GlobalClass]
public partial class ShipSpec : Resource
{
    [Export] public float Mass = 20_000f;                       // kg

    [ExportGroup("Thrust (newtons)")]
    [Export] public float MainThrust = 1_000_000f;              // forward
    [Export] public float ReverseThrust = 700_000f;
    [Export] public float LateralThrust = 700_000f;             // right and left
    [Export] public float VerticalThrust = 700_000f;            // up and down

    [ExportGroup("Power")]
    /// <summary>What the main motor draws at full thrust from the engines capacitor, if the ship has a power plant (MW).</summary>
    [Export] public float ThrustMegawatts = 4f;

    [ExportGroup("Turning (rad/s², at full torque)")]
    [Export] public float PitchAcceleration = 2.1f;
    [Export] public float RollAcceleration = 4.2f;
    [Export] public float YawAcceleration = 1.12f;

    /// <summary>Moments of inertia about the ship's own X, Y, Z (kg m²); with the accelerations they give the torques.</summary>
    [Export] public Vector3 Inertia = new(80_000f, 100_000f, 40_000f);

    [ExportGroup("Hitting things")]
    /// <summary>How much of the speed into a rock comes back out of it: 0 none (all of it crushes the hull), 1 all.</summary>
    [Export(PropertyHint.Range, "0,1")] public float Restitution = 0.2f;
    /// <summary>Sliding along a rock: how hard it holds back, against how hard the ship is pressed into it.</summary>
    [Export] public float Friction = 0.4f;
}
