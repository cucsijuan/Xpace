using System;
using Godot;

namespace Xpace.Ship;

/// <summary>
/// An interdictor as a model, a .tres in assets/ship/interdictors: the same for one carried by a ship and one fixed in
/// a sector. It thickens the medium round a ship by (Reach / distance)², on a frequency that wanders by its own program.
/// </summary>
[GlobalClass]
public partial class InterdictorSpec : Resource
{
    [Export] public string Model = "";
    /// <summary>Where the density it adds is 1 (m).</summary>
    [Export] public float Reach = 46000f;
    /// <summary>Beyond this it doesn't reach a ship (m; 0: as far as its target can be held).</summary>
    [Export] public float Range = 0f;
    /// <summary>What it draws while on (MW).</summary>
    [Export] public float Megawatts = 8f;
    [ExportGroup("Its frequency's program (0-255 band)")]
    [Export] public float Centre = 128f;
    [Export] public float Wander = 70f;
    [Export] public float Quiver = 15f;
    /// <summary>How fast the frequency wanders (radians a second of its slower swing).</summary>
    [Export] public float WanderRate = 0.12f;

    /// <summary>Where its frequency is after so many seconds: two swings, out of step.</summary>
    public byte Band(double t) =>
        (byte)Mathf.Clamp(Centre + Wander * Math.Sin(t * WanderRate) + Quiver * Math.Sin(t * WanderRate * 2.3 + 1), 0, 255);

    /// <summary>What it adds to the density at that distance squared, with that share of its power.</summary>
    public float Density(float distanceSquared, float power = 1)
    {
        if (Range > 0 && distanceSquared > Range * Range)
            return 0;
        return Xpace.Space.MediumMass.Adds(Reach * power, distanceSquared);
    }
}
