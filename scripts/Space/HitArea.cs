using Godot;

namespace Xpace.Space;

/// <summary>
/// A ship's hitboxes as the physics engine sees them: an area on the
/// rounds' layer (<see cref="Hitboxes.RoundLayer"/>) whose box shapes rounds
/// fly into. What hits it goes to the ship: the ship itself if it can take
/// damage, else the first thing beside this area that can (its signature).
/// </summary>
public partial class HitArea : Area3D, IDamageable
{
    private IDamageable _ship;

    public override void _Ready()
    {
        Monitoring = false;
        CollisionLayer = Hitboxes.RoundLayer;
        CollisionMask = 0;
    }

    public void Hit(float damage)
    {
        _ship ??= Receiver();
        _ship?.Hit(damage);
    }

    private IDamageable Receiver()
    {
        Node ship = GetParent();
        if (ship is IDamageable d)
            return d;
        foreach (Node n in ship?.GetChildren() ?? new Godot.Collections.Array<Node>())
            if (n is IDamageable other && n is not HitArea)
                return other;
        return null;
    }
}
