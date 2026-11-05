using System;
using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>One echo-maker as a radar sees it for a step: where it stood, how fast, how big, whether it can be seen.</summary>
public sealed class Blip
{
    public IRadarTarget Target;
    public Vector3 Position, Velocity;
    public float CrossSection;
    public bool Shown;
    /// <summary>Its place in the sector's list, the order radars have always met targets in.</summary>
    public int Order;
}

/// <summary>
/// Every radar target of a sector, taken once a step (see <see cref="Sectors.Built"/>) with its place in a spatial grid:
/// radars ask it who they could see instead of asking every target for its place, once each. A target reaches a radar as
/// far as the fourth root of its cross section (the radar equation), which is the grid's weight.
/// </summary>
public sealed class RadarPicture
{
    public readonly List<Blip> All = new();
    private readonly SpatialGrid<Blip> _grid = new(2000f);
    private readonly List<Blip> _near = new();

    public static RadarPicture Of(Node from) => Sectors.Built(from, "radar_targets", typeof(RadarPicture), nodes =>
    {
        var picture = new RadarPicture();
        foreach (Node node in nodes)
        {
            if (node is not IRadarTarget t)
                continue;
            var blip = new Blip
            {
                Target = t,
                Position = t.GlobalPosition,
                Velocity = t.Velocity,
                CrossSection = t.RadarCrossSection,
                Shown = t.Alive && (node is not Node3D n || n.IsVisibleInTree()),     // a destroyed ship's puppet hides
                Order = picture.All.Count,
            };
            picture.All.Add(blip);
            picture._grid.Add(blip, blip.Position, MathF.Sqrt(MathF.Sqrt(Math.Max(blip.CrossSection, 1e-6f))));
        }
        return picture;
    });

    /// <summary>
    /// The targets a radar at a point could detect at best (before jamming and clutter, which only take from it): within
    /// <paramref name="range"/> for a target of <paramref name="referenceCrossSection"/>, more for a bigger one. In the
    /// order of the sector's list.
    /// </summary>
    public List<Blip> Detectable(Vector3 from, float range, float referenceCrossSection)
    {
        _near.Clear();
        _grid.Reaching(from, range / MathF.Sqrt(MathF.Sqrt(referenceCrossSection)), _near);
        _near.Sort((a, b) => a.Order.CompareTo(b.Order));
        return _near;
    }
}
