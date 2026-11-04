using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.Ship;

/// <summary>
/// The rails of a ship's pylons, and which missile hangs on which. Radar missiles load from the first rail on and
/// heat seekers from the last one back, so a launch takes one off the end of its run and nothing moves along; the
/// rails go across the pylons in turn (the first rail of each, then the second...), so the ship stays balanced. The
/// ship's launcher and another machine's copy of the ship lay them out the same way.
/// </summary>
public static class MissileRails
{
    /// <summary>The rails of the pylons on <paramref name="hardpoints"/>, in loading order.</summary>
    public static List<Node3D> Of(IEnumerable<Hardpoint> hardpoints)
    {
        var pylons = hardpoints.Select(h => h.Rails).Where(r => r.Count > 0).ToList();
        var rails = new List<Node3D>();
        for (int k = 0; pylons.Any(p => k < p.Count); k++)
            foreach (IReadOnlyList<Node3D> pylon in pylons)
                if (k < pylon.Count)
                    rails.Add(pylon[k]);
        return rails;
    }

    /// <summary>The rail the next radar missile leaves from, with <paramref name="radar"/> on the rails (none: null).</summary>
    public static Node3D Front(IReadOnlyList<Node3D> rails, int radar) => radar > 0 && radar <= rails.Count ? rails[radar - 1] : null;

    /// <summary>The rail the next heat seeker leaves from, with <paramref name="heat"/> on the rails (none: null).</summary>
    public static Node3D Back(IReadOnlyList<Node3D> rails, int heat) => heat > 0 && heat <= rails.Count ? rails[rails.Count - heat] : null;

    /// <summary>Hangs <paramref name="radar"/> radar missiles and <paramref name="heat"/> heat seekers on the rails, as their looks.</summary>
    public static void Show(IReadOnlyList<Node3D> rails, PackedScene radarLook, int radar, PackedScene heatLook, int heat)
    {
        for (int i = 0; i < rails.Count; i++)
        {
            PackedScene want = i < radar ? radarLook : i >= rails.Count - heat ? heatLook : null;
            Node old = rails[i].GetNodeOrNull("Load");
            if (old != null && old.GetMeta("look", "").AsString() == (want?.ResourcePath ?? ""))
                continue;
            if (old != null)
            {
                rails[i].RemoveChild(old);
                old.QueueFree();
            }
            if (want?.Instantiate() is Node3D look)
            {
                look.Name = "Load";
                look.SetMeta("look", want.ResourcePath);
                rails[i].AddChild(look);
            }
        }
    }
}
