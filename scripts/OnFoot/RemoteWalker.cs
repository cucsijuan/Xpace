using Godot;
using Xpace.Net;

namespace Xpace.OnFoot;

/// <summary>
/// Another machine's player on foot, as their machine says they are (<see cref="WalkerState"/>): their body in the
/// station they walk, in its frame (this node goes below the station's node), with their name over the head. Between
/// one state and the next it goes on as the last one went, a little way at most, and eases toward where it should be,
/// so it walks smoothly; the body's animations follow its speed over the floor.
/// </summary>
public partial class RemoteWalker : Node3D
{
    /// <summary>How fast it closes on where it should be (1/s).</summary>
    [Export] public float Follow = 12f;
    /// <summary>The most it goes on past the last state (s): a lost one or two don't stop it.</summary>
    [Export] public float MaxLead = 0.25f;
    /// <summary>Further than this from where it should be (m), it is put there at once.</summary>
    [Export] public float SnapDistance = 3f;

    public BodyAnimator Body { get; private set; }

    public string Pilot
    {
        get => _name?.Text ?? "";
        set
        {
            if (_name != null)
                _name.Text = value ?? "";
        }
    }

    /// <summary>The state last heard.</summary>
    public WalkerState State { get; private set; }

    private Label3D _name;
    private double _age;
    private bool _placed;

    /// <summary>A remote walker showing <paramref name="body"/> (a scene with a <see cref="BodyAnimator"/> at its root).</summary>
    public static RemoteWalker Make(PackedScene body, string pilot)
    {
        var w = new RemoteWalker { Name = "RemoteWalker" };
        w.Body = body?.Instantiate<BodyAnimator>();
        if (w.Body != null)
            w.AddChild(w.Body);
        w._name = new Label3D
        {
            Name = "Name", Text = pilot ?? "", Position = new Vector3(0, 2.1f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 48, PixelSize = 0.004f, OutlineSize = 10, NoDepthTest = false, Modulate = new Color(0.85f, 0.95f, 1f),
        };
        w.AddChild(w._name);
        return w;
    }

    public void Hear(WalkerState state)
    {
        State = state;
        _age = 0;
        if (!_placed || Position.DistanceTo(state.Position) > SnapDistance)
        {
            Transform = new Transform3D(new Basis(state.Facing), state.Position);
            _placed = true;
        }
    }

    public override void _Process(double delta)
    {
        if (!_placed)
            return;
        _age += delta;
        WalkerState s = State;
        Vector3 target = s.Position + s.Velocity * (float)Mathf.Min(_age, MaxLead);
        float k = 1 - Mathf.Exp(-Follow * (float)delta);
        Quaternion facing = Basis.GetRotationQuaternion().Normalized().Slerp(s.Facing, k);
        Transform = new Transform3D(new Basis(facing), Position.Lerp(target, k));
        if (Body != null)
        {
            Vector3 up = s.Facing * Vector3.Up;
            Body.Speed = (s.Velocity - up * s.Velocity.Dot(up)).Length();
            Body.Grounded = s.Grounded;
        }
    }
}
