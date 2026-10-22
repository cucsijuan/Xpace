using Godot;

namespace Xpace.Ship;

/// <summary>What a hull can do: its mass and the most its thrusters give.</summary>
[GlobalClass]
public partial class ShipSpec : Resource
{
    [Export] public float Mass = 20_000f;                       // kg

    [ExportGroup("Thrust (newtons)")]
    [Export] public float MainThrust = 1_000_000f;              // forward
    [Export] public float ReverseThrust = 500_000f;
    [Export] public float LateralThrust = 500_000f;             // right and left
    [Export] public float VerticalThrust = 500_000f;            // up and down

    [ExportGroup("Turning (rad/s², at full torque)")]
    [Export] public float PitchAcceleration = 1.5f;
    [Export] public float RollAcceleration = 3.0f;
    [Export] public float YawAcceleration = 0.8f;

    /// <summary>Moments of inertia about the ship's own X, Y, Z (kg m²); with the accelerations they give the torques.</summary>
    [Export] public Vector3 Inertia = new(80_000f, 100_000f, 40_000f);
}
