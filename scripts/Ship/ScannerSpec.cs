using Godot;

namespace Xpace.Ship;

/// <summary>
/// A model of cargo scanner: how long it must stay on a ship the radar has locked to read its hold, from how far, and
/// what it draws. A better one reads sooner and from further away.
/// </summary>
[GlobalClass]
public partial class ScannerSpec : Resource
{
    [Export] public string Name = "CS-1";

    /// <summary>Seconds on the same ship, with the radar's lock held all along.</summary>
    [Export] public float Seconds = 15f;

    /// <summary>Metres: past this it reads nothing, whatever the radar can lock.</summary>
    [Export] public float Range = 8000f;

    /// <summary>What it draws from the systems capacitor while it reads; with less it takes longer.</summary>
    [Export] public float Megawatts = 0.3f;
}
