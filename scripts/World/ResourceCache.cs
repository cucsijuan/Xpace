using System.Collections.Generic;
using Godot;

namespace Xpace.World;

/// <summary>
/// Resources the game asks for again and again, by path, while it plays. Asked of the engine each time, a resource's C#
/// object can be collected between one question and the next, and the engine then swaps its handle while the finalizer
/// is at it, which kills the game. Kept here they are loaded once and stay.
/// </summary>
public static class ResourceCache
{
    private static readonly Dictionary<string, Resource> Loaded = new();

    public static T Load<T>(string path) where T : Resource
    {
        if (!Loaded.TryGetValue(path, out Resource resource))
            Loaded[path] = resource = GD.Load<T>(path);
        return resource as T;
    }
}
