using System.Collections.Generic;
using Godot;
using Xpace.Space;

namespace Xpace.Debug;

/// <summary>
/// Draws every station's and gate's law zones while <see cref="DebugOptions.LawZones"/> is on: the protected zone
/// (<see cref="StationLaws.ZoneRadius"/>, weapons fire is a crime inside) and the speed limit's radius
/// (<see cref="StationLaws.SpeedRadius"/>), each a sphere round the station's radio with its name and size written on
/// top. Read from the laws every frame, so a change in the editor or in the game shows at once. Seen from inside too.
/// Put one in the game's shell (main.tscn).
/// </summary>
public partial class LawZones : Node
{
    [Export] public Shader ZoneShader = GD.Load<Shader>("res://assets/materials/debug_zone.gdshader");
    [Export] public Color ProtectedColour = new(1f, 0.45f, 0.1f, 1f);
    [Export] public Color SpeedColour = new(0.2f, 0.8f, 1f, 1f);

    private const string Marker = "DebugLawZones";
    private readonly List<Node3D> _made = new();
    private SphereMesh _sphere;

    public override void _Ready() => DebugOptions.Changed += Refresh;

    public override void _ExitTree()
    {
        DebugOptions.Changed -= Refresh;
        Clear();
    }

    public override void _Process(double delta)
    {
        if (!DebugOptions.LawZones)
            return;
        foreach (StationRadio radio in StationRadio.All)
            if (IsInstanceValid(radio) && radio.IsInsideTree() && radio.Laws != null)
                Update(radio.GetNodeOrNull<Node3D>(Marker) ?? Make(radio), radio);
    }

    private void Refresh()
    {
        if (!DebugOptions.LawZones)
            Clear();
    }

    private void Clear()
    {
        foreach (Node3D made in _made)
            if (IsInstanceValid(made))
                made.QueueFree();
        _made.Clear();
    }

    private Node3D Make(StationRadio radio)
    {
        _sphere ??= new SphereMesh { Radius = 1, Height = 2, RadialSegments = 64, Rings = 32 };
        var zones = new Node3D { Name = Marker };
        zones.AddChild(Zone("Protected", ProtectedColour));
        zones.AddChild(Zone("Speed", SpeedColour));
        radio.AddChild(zones);
        _made.Add(zones);
        return zones;
    }

    private Node3D Zone(string name, Color colour)
    {
        var zone = new Node3D { Name = name };
        var material = new ShaderMaterial { Shader = ZoneShader };
        material.SetShaderParameter("colour", colour);
        zone.AddChild(new MeshInstance3D
        {
            Name = "Sphere", Mesh = _sphere, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        zone.AddChild(new Label3D
        {
            Name = "Label", Modulate = colour, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true,
            NoDepthTest = true, PixelSize = 0.0008f, FontSize = 28, OutlineSize = 6,
        });
        return zone;
    }

    // Sizes and words from the laws now; the zones stay upright whatever the station's turn
    private static void Update(Node3D zones, StationRadio radio)
    {
        zones.GlobalBasis = Basis.Identity;
        StationLaws laws = radio.Laws;
        Show(zones.GetNode<Node3D>("Protected"), laws.ZoneRadius, $"{radio.ShownName}  PROTECTED {laws.ZoneRadius:0} m");
        Show(zones.GetNode<Node3D>("Speed"), laws.SpeedRadius, $"{radio.ShownName}  SPEED LIMIT {laws.SpeedLimit:0} m/s  {laws.SpeedRadius:0} m");
    }

    private static void Show(Node3D zone, float radius, string text)
    {
        zone.Visible = radius > 0;
        if (radius <= 0)
            return;
        zone.GetNode<Node3D>("Sphere").Scale = Vector3.One * radius;
        var label = zone.GetNode<Label3D>("Label");
        label.Position = Vector3.Up * radius;
        label.Text = text;
    }
}
