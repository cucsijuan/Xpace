using Godot;

namespace Xpace.Space;

/// <summary>
/// A commercial beacon: a pilot within radio reach asks it for market data (COMMS: REQUEST DATA) and learns the markets
/// of the stations it reports, as they are then. A cargo ship passing within <see cref="HeardMeters"/> tells its owner
/// the same. Its radio is the child "Radio", with an id of its own.
/// </summary>
public partial class CommercialBeacon : Node3D
{
    /// <summary>How many gates away it reports (0: only its own sector).</summary>
    [Export] public int ReachJumps = 0;
    /// <summary>How far from it a passing cargo ship picks it up (m).</summary>
    [Export] public float HeardMeters = 20000f;
}
