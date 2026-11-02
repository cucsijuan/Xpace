using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// What a sector shows, left out while nobody here looks at it (a session's
/// host running it for its guests): its meshes, lights, particles and decals
/// stay nodes, for the programs and scenes that use them, but give the
/// renderer nothing to keep. What is added while it is left out is left out
/// too. Collisions never come from what is drawn, so nothing else changes.
/// </summary>
public sealed class SectorVisuals
{
    private readonly Node _root;
    private readonly Dictionary<VisualInstance3D, Rid> _left = new();

    public SectorVisuals(Node root) => _root = root;

    /// <summary>Its visuals are left out now.</summary>
    public bool Hidden { get; private set; }

    /// <summary>How many things are left out now.</summary>
    public int Count => _left.Count;

    public void Hide()
    {
        if (Hidden)
            return;
        Hidden = true;
        LeaveOut(_root);
        _root.GetTree().NodeAdded += Added;
    }

    public void Show()
    {
        if (!Hidden)
            return;
        Hidden = false;
        if (_root.IsInsideTree())
            _root.GetTree().NodeAdded -= Added;
        foreach (var (node, rendered) in _left)
            if (GodotObject.IsInstanceValid(node))
                node.SetBase(rendered);
        _left.Clear();
    }

    /// <summary>The sector is going: nothing to bring back.</summary>
    public void Forget()
    {
        if (Hidden && _root.IsInsideTree())
            _root.GetTree().NodeAdded -= Added;
        Hidden = false;
        _left.Clear();
    }

    private void Added(Node node)
    {
        if (node is VisualInstance3D && _root.IsAncestorOf(node))
            Callable.From(() => LeaveOutOne(node)).CallDeferred();      // once it has what it shows
    }

    private void LeaveOut(Node node)
    {
        LeaveOutOne(node);
        foreach (Node child in node.GetChildren())
            LeaveOut(child);
    }

    private void LeaveOutOne(Node node)
    {
        if (!Hidden || node is not VisualInstance3D visual || !GodotObject.IsInstanceValid(visual) || _left.ContainsKey(visual))
            return;
        Rid rendered = visual.GetBase();
        if (!rendered.IsValid)
            return;
        _left[visual] = rendered;
        visual.SetBase(new Rid());
    }
}
