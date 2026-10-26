using Godot;

namespace Xpace.Menu;

/// <summary>
/// The main menu's space dust: specks in a box round the ship going past
/// it at its <see cref="MenuShip.Velocity"/>, the other way, so the ship
/// that stays put seems to fly (faster while it pushes, slowing as it
/// coasts). Each speck is drawn as where it was over the last
/// <see cref="TrailSeconds"/>, so they stretch a little as the ship speeds up.
/// A speck leaving the box comes back in on the other side; specks fade out
/// towards the box's faces, so none pops in.
/// </summary>
public partial class MenuDust : MultiMeshInstance3D
{
    [Export] public NodePath ShipPath = "../Ship";
    [Export] public float Spread = 60f;                 // m, half the box
    [Export] public int Specks = 900;
    [Export] public float Size = 0.15f;                 // m
    [Export] public float TrailSeconds = 0.012f;        // how long a streak lasts
    [Export] public Color Tint = new(0.8f, 0.85f, 0.9f, 0.55f);

    private MenuShip _ship;
    private Vector3[] _at;                              // relative to the ship

    public override void _Ready()
    {
        _ship = GetNodeOrNull<MenuShip>(ShipPath);
        var rng = new RandomNumberGenerator { Seed = 7 };
        _at = new Vector3[Specks];
        for (int i = 0; i < Specks; i++)
            _at[i] = new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(-1, 1), rng.RandfRange(-1, 1)) * Spread;
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
                    VertexColorUseAsAlbedo = true,
                    AlbedoTexture = Round(),
                },
            },
        };
        CustomAabb = new Aabb(-Vector3.One * Spread * 2, Vector3.One * Spread * 4);
        Place();
    }

    public override void _Process(double delta)
    {
        Vector3 step = (_ship?.Velocity ?? Vector3.Zero) * (float)delta;
        float span = 2 * Spread;
        for (int i = 0; i < _at.Length; i++)
        {
            Vector3 p = _at[i] - step;
            p.X = Mathf.PosMod(p.X + Spread, span) - Spread;
            p.Y = Mathf.PosMod(p.Y + Spread, span) - Spread;
            p.Z = Mathf.PosMod(p.Z + Spread, span) - Spread;
            _at[i] = p;
        }
        Place();
    }

    /// <summary>A soft round speck: white in the middle, nothing at the rim.</summary>
    private static Texture2D Round() => new GradientTexture2D
    {
        Width = 32,
        Height = 32,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(0.5f, 0f),
        Gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.35f, 1f },
            Colors = new[] { Colors.White, new Color(1, 1, 1, 0.6f), new Color(1, 1, 1, 0) },
        },
    };

    private void Place()
    {
        Vector3 centre = _ship?.GlobalPosition ?? Vector3.Zero;
        GlobalTransform = new Transform3D(Basis.Identity, centre);
        Vector3 eye = (GetViewport()?.GetCamera3D()?.GlobalPosition ?? centre + Vector3.Back) - centre;
        Vector3 velocity = _ship?.Velocity ?? Vector3.Zero;
        float speed = velocity.Length();
        Vector3 along = speed > 1e-3f ? velocity / speed : Vector3.Right;
        float stretch = 1 + speed * TrailSeconds / Size;
        for (int i = 0; i < _at.Length; i++)
        {
            Vector3 p = _at[i];
            float edge = Mathf.Max(Mathf.Abs(p.X), Mathf.Max(Mathf.Abs(p.Y), Mathf.Abs(p.Z))) / Spread;
            float fade = Mathf.Clamp((1 - edge) / 0.25f, 0, 1);
            // The quad faces the eye, its length along the flight: the speck now at one end, where it was at the other
            Vector3 toEye = (eye - p).Normalized();
            Vector3 length = along - toEye * along.Dot(toEye);
            if (length.LengthSquared() < 1e-6f)
                length = toEye.Cross(Vector3.Up);
            length = length.Normalized();
            var basis = new Basis(length * stretch, toEye.Cross(length), toEye);
            Multimesh.SetInstanceTransform(i, new Transform3D(basis, p + along * (speed * TrailSeconds / 2)));
            Multimesh.SetInstanceColor(i, new Color(Tint, Tint.A * fade));
        }
    }
}
