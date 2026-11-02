using Godot;
using Xpace.Ship;

namespace Xpace.Space;

/// <summary>A ship whose cruise shows on it: how much it is stirring the medium now, 0 to 1.</summary>
public interface ICruiseShown
{
    float WakeLevel { get; }
}

/// <summary>
/// The long wake a cruising ship leaves in the medium: a ribbon along where it
/// has been, fading with age, on any ship (its owner's machine or another's).
/// Far away it keeps a width on the screen, so a pilot can pick it out by eye.
/// Put it under the ship; it stays in the world, and draws nothing in a
/// sector nobody here looks at.
/// </summary>
public partial class CruiseWake : MeshInstance3D, IOriginShift
{
    private const int Points = 48, Sections = 72;

    [Export] public float Seconds = 7f;                 // how long it lasts
    [Export] public float Width = 22f;                  // m, half across, at its widest
    [Export] public float TipShare = 0.05f;             // the share of its length the rounded tip takes
    [Export] public float TailThinning = 1.1f;          // how fast it thins towards the end
    [Export] public float FarWidth = 0.007f;            // half across at the least, as a share of the distance
    [Export] public float Brightness = 1.6f;
    [Export] public Color Head = new(0.45f, 0.85f, 1f);
    [Export] public Color Tail = new(0.8f, 0.3f, 1f);

    private readonly Vector3[] _at = new Vector3[Points];
    private readonly float[] _level = new float[Points];
    private int _newest, _count;
    private double _sinceLast;
    private ICruiseShown _ship;
    private Node3D _hull;
    private ImmediateMesh _mesh;

    public override void _Ready()
    {
        _hull = GetParent<Node3D>();
        _ship = _hull as ICruiseShown;
        TopLevel = true;
        GlobalTransform = Transform3D.Identity;
        _mesh = new ImmediateMesh();
        Mesh = _mesh;
        CastShadow = ShadowCastingSetting.Off;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/materials/cruise_wake.gdshader") };
        material.SetShaderParameter("brightness", Brightness);
        MaterialOverride = material;
        IgnoreOcclusionCulling = true;
        ExtraCullMargin = 16384;
    }

    public void ShiftOrigin(Vector3 offset)
    {
        for (int i = 0; i < _count; i++)
            _at[i] -= offset;
        GlobalTransform = Transform3D.Identity;
    }

    public override void _Process(double delta)
    {
        if (_ship == null)
            return;
        float level = _ship.WakeLevel;
        _sinceLast += delta;
        double every = Seconds / (Points - 1);
        if (_sinceLast >= every)
        {
            _sinceLast = 0;
            _newest = (_newest + 1) % Points;
            _at[_newest] = _hull.GlobalPosition;
            _level[_newest] = level;
            if (_count < Points)
                _count++;
        }
        Draw(level, (float)(_sinceLast / every));
    }

    private void Draw(float level, float share)
    {
        _mesh.ClearSurfaces();
        bool any = level > 0.01f;
        for (int i = 0; i < _count && !any; i++)
            any = _level[i] > 0.01f;
        Camera3D camera = GetViewport()?.GetCamera3D();
        if (!any || _count < 2 || camera == null || !SectorHost.Seen(this))
            return;
        Vector3 eye = camera.GlobalPosition;
        float oldest = (_count - 1 + share) / (Points - 1);
        _mesh.SurfaceBegin(Godot.Mesh.PrimitiveType.TriangleStrip);
        Vector3 before = _hull.GlobalPosition;
        for (int k = 0; k <= Sections; k++)
        {
            // Close together near the ship, where the tip rounds off
            float age = Mathf.Min(k / (float)Sections * (k / (float)Sections), oldest);
            Sample(age, share, level, out Vector3 p, out float strength);
            Sample(Mathf.Min(age + 0.01f, oldest), share, level, out Vector3 further, out _);
            Vector3 toEye = eye - p;
            Vector3 across = (further - before).Cross(toEye);
            if (across.LengthSquared() < 1e-3f)
                across = Vector3.Up;
            float half = Mathf.Max(Width, toEye.Length() * FarWidth) * Profile(age);
            across = across.Normalized() * Mathf.Max(half, 0.01f);
            Color c = Head.Lerp(Tail, age);
            c.A = strength * Mathf.Clamp((1 - age) * 3, 0, 1);
            _mesh.SurfaceSetColor(c);
            _mesh.SurfaceSetUV(new Vector2(0, age));
            _mesh.SurfaceAddVertex(p + across);
            _mesh.SurfaceSetColor(c);
            _mesh.SurfaceSetUV(new Vector2(1, age));
            _mesh.SurfaceAddVertex(p - across);
            before = p;
        }
        _mesh.SurfaceEnd();
    }

    /// <summary>Its width along it, 0 to 1: rounded at the ship, thinning to nothing at the end.</summary>
    private float Profile(float age)
    {
        float tip = Mathf.Sqrt(Mathf.Clamp(age / TipShare, 0, 1));
        return tip * Mathf.Pow(1 - age, TailThinning);
    }

    // Where the wake is at an age, between the points it has laid
    private void Sample(float age, float share, float level, out Vector3 at, out float strength)
    {
        float place = age * (Points - 1) - share;       // -1 at the ship itself
        int i = Mathf.Clamp(Mathf.FloorToInt(place), -1, _count - 2);
        float u = Mathf.Clamp(place - i, 0, 1);
        Vector3 from = i < 0 ? _hull.GlobalPosition : _at[(_newest - i + Points) % Points];
        float was = i < 0 ? level : _level[(_newest - i + Points) % Points];
        int next = (_newest - (i + 1) + Points) % Points;
        at = from.Lerp(_at[next], u);
        strength = Mathf.Lerp(was, _level[next], u);
    }
}
