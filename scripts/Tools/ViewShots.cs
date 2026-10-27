using System.Threading.Tasks;
using Godot;

namespace Xpace.Tools;

/// <summary>
/// Takes pictures of a scene from set points of view and quits: a camera
/// child is moved to each <see cref="Eyes"/> entry in turn, looking at the
/// matching <see cref="Targets"/> one, and every frame is saved as a PNG in
/// <see cref="Folder"/> (user://). Needs a window (not --headless).
///
///   godot --path . res://tools/station_bay/preview.tscn
/// </summary>
public partial class ViewShots : Node3D
{
    [Export] public string Folder = "user://shots";
    [Export] public string[] Names = [];
    [Export] public Vector3[] Eyes = [];
    [Export] public Vector3[] Targets = [];
    [Export] public float[] Fovs = [];

    /// <summary>Frames rendered before each picture (lets lights and glow settle).</summary>
    [Export] public int Settle = 8;

    public override async void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(Folder);
        var camera = GetNode<Camera3D>("Camera");
        for (int i = 0; i < Names.Length; i++)
        {
            camera.Fov = i < Fovs.Length ? Fovs[i] : 70;
            camera.LookAtFromPosition(Eyes[i], Targets[i]);
            await Frames(Settle);
            GetViewport().GetTexture().GetImage().SavePng($"{Folder}/{Names[i]}.png");
        }
        GD.Print("Saved in ", ProjectSettings.GlobalizePath(Folder));
        GetTree().Quit();
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
