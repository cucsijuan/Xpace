using Godot;

namespace Xpace.Space;

/// <summary>
/// Slides meshes of an imported model, such as a hangar's doors: a child of
/// the model's instance that moves the named meshes by <see cref="Travel"/>
/// (in the model's frame) times <see cref="Open"/>, over
/// <see cref="Seconds"/> whenever Open changes. Like <see cref="ModelPaint"/>,
/// it is the outer scene's own node, so an export keeps it.
/// </summary>
public partial class MeshSlide : Node
{
    [Export] public string[] Meshes = [];
    [Export] public Vector3 Travel = new(0, 11.5f, 0);
    [Export(PropertyHint.Range, "0,1")] public float Open = 1;
    [Export] public float Seconds = 6;

    /// <summary>How open the meshes are now, 0 to 1 (they move towards <see cref="Open"/>).</summary>
    public float At => _at;

    private Node3D[] _parts = [];
    private Vector3[] _shut = [];
    private float _at;

    public override void _Ready()
    {
        if (GetParent() is not Node model)
            return;
        _parts = new Node3D[Meshes.Length];
        _shut = new Vector3[Meshes.Length];
        for (int i = 0; i < Meshes.Length; i++)
        {
            _parts[i] = model.FindChild(Meshes[i], true, false) as Node3D;
            if (_parts[i] != null)
                _shut[i] = _parts[i].Position;
        }
        _at = Open;
        Place();
    }

    public override void _Process(double delta)
    {
        if (_at == Open)
            return;
        _at = Mathf.MoveToward(_at, Open, (float)delta / Mathf.Max(Seconds, 0.01f));
        Place();
    }

    private void Place()
    {
        for (int i = 0; i < _parts.Length; i++)
            if (_parts[i] != null)
                _parts[i].Position = _shut[i] + Travel * _at;
    }
}
