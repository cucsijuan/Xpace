using Godot;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// What the pilot sees going by in cruise: specks of the medium in a box round
/// the eye, each drawn as where it was over the last <see cref="TrailSeconds"/>,
/// so they stretch into streaks, in the colours of the veil; more of them, and
/// brighter, the thinner it is. They keep off the line of flight, the walls of a
/// tunnel the ship goes down, so none comes from one point ahead and none
/// passes near the eye. Nothing at flying speed, where the plain dust
/// (<see cref="DustPath"/>) shows how the ship moves; that one it hides meanwhile.
/// </summary>
public partial class CruiseDust : MultiMeshInstance3D
{
    [Export] public NodePath ShipPath = "..";
    [Export] public NodePath DustPath = "../Dust";
    [Export] public float Spread = 450f;                // m, half the box
    [Export] public int Specks = 1600;
    [Export] public float Size = 0.7f;                  // m
    [Export] public float TrailSeconds = 0.035f;
    [Export] public float LongestStreak = 110f;         // m
    [Export] public float TunnelRadius = 120f;          // m: the nearest they come to the line of flight
    [Export] public Color[] Colours =
    {
        new(0.75f, 0.9f, 1f, 0.9f), new(0.35f, 0.8f, 1f, 0.9f), new(0.85f, 0.4f, 1f, 0.85f),
        new(1f, 0.7f, 0.35f, 0.8f), new(0.6f, 1f, 0.8f, 0.7f),
    };

    private ShipBody _ship;
    private Node3D _dust;
    private Vector3[] _at;
    private Color[] _tint;

    public override void _Ready()
    {
        _ship = GetNodeOrNull<ShipBody>(ShipPath) ?? ShipBody.Of(this);
        _dust = GetNodeOrNull<Node3D>(DustPath);
        TopLevel = true;
        var rng = new RandomNumberGenerator { Seed = 11 };
        _at = new Vector3[Specks];
        _tint = new Color[Specks];
        for (int i = 0; i < Specks; i++)
        {
            _at[i] = new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(-1, 1), rng.RandfRange(-1, 1)) * Spread;
            // Mostly pale, the colours here and there
            int colour = rng.Randf() < 0.55f ? 0 : rng.RandiRange(1, Colours.Length - 1);
            _tint[i] = Colours[colour];
        }
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            InstanceCount = Specks,
            Mesh = new QuadMesh
            {
                Size = new Vector2(Size, Size),
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    VertexColorUseAsAlbedo = true,
                    AlbedoTexture = new GradientTexture2D
                    {
                        Width = 32,
                        Height = 32,
                        Fill = GradientTexture2D.FillEnum.Radial,
                        FillFrom = new Vector2(0.5f, 0.5f),
                        FillTo = new Vector2(0.5f, 0f),
                        Gradient = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { Colors.White, new Color(1, 1, 1, 0) } },
                    },
                },
            },
        };
        CastShadow = ShadowCastingSetting.Off;
        CustomAabb = new Aabb(-Vector3.One * Spread * 2, Vector3.One * Spread * 4);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        float level = _ship?.WakeLevel ?? 0;
        bool on = level > 0.01f;
        Visible = on;
        if (_dust != null)
            _dust.Visible = level < 0.15f;
        Camera3D camera = GetViewport()?.GetCamera3D();
        if (!on || camera == null)
            return;
        Vector3 eye = camera.GlobalPosition;
        GlobalTransform = new Transform3D(Basis.Identity, eye);
        Vector3 velocity = _ship.LinearVelocity;
        float speed = velocity.Length();
        Vector3 along = speed > 1e-3f ? velocity / speed : Vector3.Forward;
        Vector3 step = velocity * (float)delta;
        float span = 2 * Spread;
        float length = Mathf.Min(speed * TrailSeconds, LongestStreak);
        float stretch = 1 + length / Size;
        int shown = (int)(Specks * Mathf.Clamp(level * 1.3f, 0, 1));
        Vector3 lineSide = along.Cross(Mathf.Abs(along.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        for (int i = 0; i < Specks; i++)
        {
            Vector3 p = _at[i] - step;
            p.X = Mathf.PosMod(p.X + Spread, span) - Spread;
            p.Y = Mathf.PosMod(p.Y + Spread, span) - Spread;
            p.Z = Mathf.PosMod(p.Z + Spread, span) - Spread;
            _at[i] = p;
            float edge = Mathf.Max(Mathf.Abs(p.X), Mathf.Max(Mathf.Abs(p.Y), Mathf.Abs(p.Z))) / Spread;
            float fade = i < shown ? Mathf.Clamp((1 - edge) / 0.3f, 0, 1) : 0;
            // Out to the tunnel's wall: its distance from the line, r, becomes sqrt(R² + r²)
            float ahead = p.Dot(along);
            Vector3 off = p - along * ahead;
            float r = off.Length();
            off = (r > 1e-3f ? off / r : lineSide) * Mathf.Sqrt(TunnelRadius * TunnelRadius + r * r);
            Vector3 middle = along * (ahead + length / 2) + off;
            Vector3 toEye = -middle.Normalized();
            Vector3 lengthAxis = along - toEye * along.Dot(toEye);
            if (lengthAxis.LengthSquared() < 1e-6f)
                lengthAxis = toEye.Cross(Vector3.Up);
            lengthAxis = lengthAxis.Normalized();
            float wide = 1 + middle.Length() * 0.004f;  // a far streak stays a line, not a thread
            var basis = new Basis(lengthAxis * stretch, toEye.Cross(lengthAxis) * wide, toEye);
            Multimesh.SetInstanceTransform(i, new Transform3D(basis, middle));
            Color c = _tint[i];
            c.A *= fade * level;
            Multimesh.SetInstanceColor(i, c);
        }
    }
}
