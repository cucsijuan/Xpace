using Godot;

namespace Xpace.Ship;

/// <summary>
/// A set of manoeuvring thrusters (a .tres per model, in
/// assets/ship/thrusters): what they push sideways, up and down and astern,
/// and how hard they turn the ship. With the hull they are fitted to
/// (<see cref="ShipSpec"/>: its mass, its inertia, its veil) they give the
/// ship's accelerations, its top speeds that way and its top turn rates,
/// and how tight it can turn without sliding at a speed: nothing of it is
/// written in any program.
/// </summary>
[GlobalClass]
public partial class ThrusterSpec : Resource
{
    [Export] public string Model = "T-35";

    [ExportGroup("Push (newtons)")]
    [Export] public float ReverseThrust = 700_000f;
    [Export] public float LateralThrust = 1_000_000f;     // right and left: yaw bends the path with these
    [Export] public float VerticalThrust = 2_000_000f;  // up and down: pitch bends the path with these

    /// <summary>Turning, newton metres: about the ship's X (pitch), Y (yaw), Z (roll).</summary>
    [Export] public Vector3 Torque = new(168_000f, 112_000f, 168_000f);
}
