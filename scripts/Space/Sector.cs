using Godot;

namespace Xpace.Space;

/// <summary>
/// A sector of the map: a solar system with its own scene (its root a
/// <see cref="FloatingOrigin"/>), played in a world of its own.
/// </summary>
[GlobalClass]
public partial class Sector : Resource
{
    /// <summary>Its name in records and messages (stable: saved games and the network use it).</summary>
    [Export] public string Id = "";
    /// <summary>Its name as players see it.</summary>
    [Export] public string Title = "";
    /// <summary>Its scene, loaded when someone goes there.</summary>
    [Export(PropertyHint.File, "*.tscn")] public string Scene = "";
}
