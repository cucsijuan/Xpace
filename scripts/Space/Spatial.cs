using System;
using Godot;

namespace Xpace.Space;

/// <summary>
/// The spatial partition of the game: one <see cref="SpatialGrid{T}"/> per kind of thing that has a place and is asked
/// about by distance. A layer is a group of nodes, the cell size that suits how far they are asked about, and where each
/// one stands. Anything that scans "everyone in range" should ask a layer instead of walking its group, and a new kind of
/// thing gets its own layer here, so the cost of asking stays with the few near, not with all.
///
/// The grids are made lazily, once a physics step, for each sector (see <see cref="Sectors.GridOf"/>), from where things
/// stood when the first question of the step came; use them to pick who is near, and test the exact distance at the time
/// of use.
/// </summary>
public static class Spatial
{
    public sealed record Layer(StringName Group, float Cell, Func<Node, Vector3> Where);

    /// <summary>Every radio, a station's or a ship's.</summary>
    public static readonly Layer Radios = new(Radio.Group, 2000f,
        n => n is IRadio radio ? radio.RadioPosition : n is Node3D n3 ? n3.GlobalPosition : Vector3.Zero);

    /// <summary>Every missile in flight.</summary>
    public static readonly Layer Missiles = new("missiles", 5000f, n => n is Node3D n3 ? n3.GlobalPosition : Vector3.Zero);

    /// <summary>The layer's grid for the sector of <paramref name="from"/>.</summary>
    public static SpatialGrid<Node> Grid(Node from, Layer layer) => Sectors.GridOf(from, layer.Group, layer.Cell, layer.Where);
}
