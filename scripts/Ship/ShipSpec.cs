using Godot;

namespace Xpace.Ship;

/// <summary>
/// What a hull is: its mass and inertia, how its veil drags, what its
/// structure takes, and the manoeuvring thrusters fitted to it
/// (<see cref="Thrusters"/>; without them, the plain set written here).
/// The main engine is its own device (<see cref="EngineDevice"/>; without
/// one, <see cref="MainThrust"/>). Its top speeds and top turn rates are not
/// written anywhere: they are where the thrust meets the veil's drag.
/// </summary>
[GlobalClass]
public partial class ShipSpec : Resource
{
    [Export] public float Mass = 20_000f;                       // kg

    /// <summary>The manoeuvring thrusters fitted (a tier); none: the plain set below.</summary>
    [Export] public ThrusterSpec Thrusters;

    [ExportGroup("Thrust (newtons): with no engine device, and no thrusters set")]
    [Export] public float MainThrust = 1_000_000f;              // forward
    [Export] public float ReverseThrust = 700_000f;
    [Export] public float LateralThrust = 700_000f;             // right and left
    [Export] public float VerticalThrust = 700_000f;            // up and down

    [ExportGroup("The veil (GDD 6.5)")]
    /// <summary>
    /// The veil's drag, per metre: a deceleration of k v² through the
    /// sector's medium. With the main thrust it sets the top speed,
    /// sqrt(thrust / mass / k): 3.1e-4 and 50 m/s² give about 400 m/s.
    /// </summary>
    [Export] public float VeilDrag = 3.1e-4f;
    /// <summary>
    /// The veil against turning, per radian, about the ship's X (pitch), Y
    /// (yaw) and Z (roll): a deceleration of k w². With the thrusters' turn
    /// it sets the top turn rate, sqrt(torque / inertia / k).
    /// </summary>
    [Export] public Vector3 VeilSpin = new(0.93f, 1.75f, 0.47f);

    [ExportGroup("Structure")]
    /// <summary>The most the hull is rated to take along its nose (m/s²): an engine that pushes harder breaks it (the engine's controller does not know).</summary>
    [Export] public float RatedAcceleration = 75f;
    /// <summary>What thrust beyond the rating does to the hull: points a second at twice the rated acceleration (past the shields).</summary>
    [Export] public float StressDamagePerSecond = 40f;

    [ExportGroup("Power")]
    /// <summary>What the main motor draws at full thrust from the engines capacitor, if the ship has a power plant (MW).</summary>
    [Export] public float ThrustMegawatts = 4f;

    [ExportGroup("Turning (rad/s², at full torque): with no thrusters set")]
    [Export] public float PitchAcceleration = 2.1f;
    [Export] public float RollAcceleration = 4.2f;
    [Export] public float YawAcceleration = 1.12f;

    /// <summary>Moments of inertia about the ship's own X, Y, Z (kg m²); with the accelerations they give the torques.</summary>
    [Export] public Vector3 Inertia = new(80_000f, 100_000f, 40_000f);

    // What the thrusters fitted give (or the plain set)
    public float Reverse => Thrusters?.ReverseThrust ?? ReverseThrust;
    public float Lateral => Thrusters?.LateralThrust ?? LateralThrust;
    public float Vertical => Thrusters?.VerticalThrust ?? VerticalThrust;

    /// <summary>Turning torques at full, about X (pitch), Y (yaw), Z (roll), N m.</summary>
    public Vector3 TurnTorque => Thrusters?.Torque ??
        new Vector3(PitchAcceleration * Inertia.X, YawAcceleration * Inertia.Y, RollAcceleration * Inertia.Z);

    /// <summary>Turning accelerations at full, rad/s².</summary>
    public Vector3 TurnAcceleration => TurnTorque / Inertia;

    /// <summary>Where a push (N) meets the veil's drag (m/s); no veil: no top.</summary>
    public float TopSpeed(float force) => VeilDrag > 0 ? Mathf.Sqrt(force / Mass / VeilDrag) : float.PositiveInfinity;

    /// <summary>Where the full turn meets the veil, per axis (rad/s); no veil about an axis: no top.</summary>
    public Vector3 TopTurnRate()
    {
        Vector3 a = TurnAcceleration;
        float Top(float acc, float k) => k > 0 ? Mathf.Sqrt(acc / k) : float.PositiveInfinity;
        return new Vector3(Top(a.X, VeilSpin.X), Top(a.Y, VeilSpin.Y), Top(a.Z, VeilSpin.Z));
    }

    [ExportGroup("Hitting things")]
    /// <summary>How much of the speed into a rock comes back out of it: 0 none (all of it crushes the hull), 1 all.</summary>
    [Export(PropertyHint.Range, "0,1")] public float Restitution = 0.2f;
    /// <summary>Sliding along a rock: how hard it holds back, against how hard the ship is pressed into it.</summary>
    [Export] public float Friction = 0.4f;
}
