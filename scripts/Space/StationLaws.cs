using Godot;

namespace Xpace.Space;

/// <summary>
/// The laws a station keeps round itself (a .tres per set of laws, in
/// assets/stations/laws), sent by its radio (<see cref="StationRadio"/>) to
/// every ship that comes into its zone, and again to any that asks.
///
/// Inside <see cref="ZoneRadius"/> the station protects: no weapon may go
/// off there (its security warns, then its police engage; see
/// <see cref="StationSecurity"/>). Nearer, inside <see cref="SpeedRadius"/>,
/// ships keep to <see cref="SpeedLimit"/> (a ship's parking mode does it
/// for its pilot); going faster is fined.
/// </summary>
[GlobalClass]
public partial class StationLaws : Resource
{
    /// <summary>How far from the station it protects, metres.</summary>
    [Export] public float ZoneRadius = 5000f;

    /// <summary>How far from the station the speed limit holds, metres.</summary>
    [Export] public float SpeedRadius = 1500f;

    /// <summary>The fastest a ship may fly inside <see cref="SpeedRadius"/>, m/s.</summary>
    [Export] public float SpeedLimit = 50f;
}
