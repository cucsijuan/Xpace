using Godot;

namespace Xpace.Space;

/// <summary>A jammer pod: always on, noise all round. Put it on (under) something to make it hard to track.</summary>
public partial class JammerPod : Node3D, IJammer
{
    [Export] public float JammingAt1Km = 400f;

    public IRadarTarget Carrier { get; private set; }

    public float NoiseToward(Vector3 point) => JammingAt1Km;

    public override void _Ready()
    {
        AddToGroup("jammers");
        for (Node n = GetParent(); n != null && Carrier == null; n = n.GetParent())
            Carrier = n as IRadarTarget;
    }
}
