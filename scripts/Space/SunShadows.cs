using Godot;
using Xpace.Menu;
using Xpace.OnFoot;

namespace Xpace.Space;

/// <summary>
/// Under a sector's sun: spends its shadow map where the player is looking
/// from. A pilot wants a station's shadows out to where it is still a big
/// thing in the canopy (the cockpit has <see cref="Xpace.Cockpit.CockpitSun"/>);
/// someone on foot wants sharp ones within a hangar. With the graphics
/// settings' low shadows the sun has only two cascades: they take the middle
/// and far reaches.
///
/// Keep the sun's angular distance at 0: its soft shadows widen with the gap
/// between what casts and what receives, and past the shadow's own size they
/// light up its middle (rocks far apart showed a ring of shadow).
/// </summary>
public partial class SunShadows : Node
{
    [Export] public ShadowReach Flying;
    [Export] public ShadowReach OnFoot;

    private DirectionalLight3D _sun;
    private ShadowReach _shown;
    private bool _low;

    public override void _Ready() => _sun = GetParent() as DirectionalLight3D;

    public override void _Process(double delta)
    {
        if (_sun == null)
            return;
        bool walking = Walker.Local?.Camera?.Current == true;
        ShadowReach reach = walking && OnFoot != null ? OnFoot : Flying;
        bool low = GameSettings.Current?.Shadows == Quality.Low;
        if (reach == null || (reach == _shown && low == _low))
            return;
        _shown = reach;
        _low = low;
        _sun.ShadowBlur = reach.Blur;
        if (low)
        {
            _sun.DirectionalShadowMaxDistance = reach.Far;
            _sun.DirectionalShadowSplit1 = reach.Middle / reach.Far;
        }
        else
        {
            _sun.DirectionalShadowMaxDistance = reach.Max;
            _sun.DirectionalShadowSplit1 = reach.Near / reach.Max;
            _sun.DirectionalShadowSplit2 = reach.Middle / reach.Max;
            _sun.DirectionalShadowSplit3 = reach.Far / reach.Max;
        }
    }
}
