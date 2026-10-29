using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A hangar's number painted on its door: digits of a <see cref="GlyphFont"/>,
/// <see cref="Height"/> tall, centred on this node and facing its +Z, in
/// <see cref="Material"/> (assets/materials/sign_paint.gdshader: the paint's
/// colour is the material's, the font only says where it goes). The number
/// is the <see cref="HangarDock"/>'s found beside it (the station numbers its
/// docks as it starts), written with <see cref="Digits"/> at least ("01");
/// <see cref="Preview"/> stands in for it in the editor. With a
/// <see cref="Door"/> it rides up with the door's meshes.
/// </summary>
[Tool]
public partial class HangarMark : Node3D
{
    private Resource _font;
    private int _preview = 1;
    private int _digits = 2;
    private float _height = 9;
    private ShaderMaterial _material;

    /// <summary>A <see cref="GlyphFont"/>.</summary>
    [Export(PropertyHint.ResourceType, nameof(GlyphFont))] public Resource Font { get => _font; set { _font = value; Build(); } }
    [Export] public int Preview { get => _preview; set { _preview = value; Build(); } }
    [Export] public int Digits { get => _digits; set { _digits = value; Build(); } }
    /// <summary>Height of the digits, metres.</summary>
    [Export] public float Height { get => _height; set { _height = value; Build(); } }
    [Export] public ShaderMaterial Material { get => _material; set { _material = value; Build(); } }
    [Export] public MeshSlide Door;

    private MeshInstance3D _mesh;
    private HangarDock _dock;
    private int _shown = -1;
    private Vector3 _rest;

    public override void _Ready()
    {
        _rest = Position;
        if (!Engine.IsEditorHint())
            _dock = GetParent()?.FindChildren("*", "", true, false).OfType<HangarDock>().FirstOrDefault();
        Build();
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
            return;
        if (_dock != null && _dock.Number != 0 && _dock.Number != _shown)
            Build();
        if (Door != null)
            Position = _rest + Door.Travel * Door.At;
    }

    private int Number => Engine.IsEditorHint() || _dock == null || _dock.Number == 0 ? _preview : _dock.Number;

    private void Build()
    {
        if (!IsInsideTree())
            return;
        if (_mesh == null)
        {
            _mesh = new MeshInstance3D();
            AddChild(_mesh, false, InternalMode.Front);
        }
        _shown = Number;
        if (_font == null || _material == null)
        {
            _mesh.Mesh = null;
            return;
        }
        string text = Mathf.Max(_shown, 0).ToString().PadLeft(Mathf.Max(_digits, 1), '0');
        int[] digits = text.Select(c => c - '0').ToArray();
        float gap = GlyphFont.SpacingOf(_font) * _height;
        float width = digits.Sum(d => GlyphFont.Aspect(_font, d) * _height) + gap * (digits.Length - 1);

        // A surface per picture the digits come from, each with the paint
        // reading that picture.
        var surfaces = new Dictionary<Texture2D, SurfaceTool>();
        float x = -width / 2;
        foreach (int d in digits)
        {
            var (texture, uv) = GlyphFont.Glyph(_font, d);
            float w = GlyphFont.Aspect(_font, d) * _height;
            if (texture != null)
            {
                if (!surfaces.TryGetValue(texture, out var st))
                {
                    st = new SurfaceTool();
                    st.Begin(Mesh.PrimitiveType.Triangles);
                    st.SetNormal(Vector3.Back);
                    surfaces[texture] = st;
                }
                Quad(st, x, x + w, uv);
            }
            x += w + gap;
        }
        var mesh = new ArrayMesh();
        foreach (var (texture, st) in surfaces)
        {
            st.Commit(mesh);
            var paint = (ShaderMaterial)_material.Duplicate();
            paint.SetShaderParameter("glyphs", texture);
            paint.SetShaderParameter("cover_from_alpha", GlyphFont.CoversFromAlpha(_font));
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, paint);
        }
        _mesh.Mesh = mesh;
    }

    private void Quad(SurfaceTool st, float x0, float x1, Rect2 uv)
    {
        float h = _height / 2;
        Vector3 a = new(x0, -h, 0), b = new(x1, -h, 0), c = new(x1, h, 0), d = new(x0, h, 0);
        Vector2 ua = new(uv.Position.X, uv.End.Y), ub = uv.End, uc = new(uv.End.X, uv.Position.Y), ud = uv.Position;
        // Clockwise as seen from +Z: Godot's front faces.
        st.SetUV(ua); st.AddVertex(a);
        st.SetUV(uc); st.AddVertex(c);
        st.SetUV(ub); st.AddVertex(b);
        st.SetUV(ua); st.AddVertex(a);
        st.SetUV(ud); st.AddVertex(d);
        st.SetUV(uc); st.AddVertex(c);
    }
}
