using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Something with a radio: a station's (<see cref="StationRadio"/>) or a
/// ship's (<see cref="Xpace.Ship.RadioDevice"/>). It is heard as far as
/// <see cref="RadioRange"/> reaches (times the listener's sensitivity), and
/// only while it is on.
/// </summary>
public interface IRadio
{
    /// <summary>Up to 8 characters, as the other radios list it.</summary>
    string Callsign { get; }
    /// <summary>'S' a station, 'V' a vessel.</summary>
    char Kind { get; }
    Vector3 RadioPosition { get; }
    /// <summary>How far a plain receiver hears it, metres.</summary>
    float RadioRange { get; }
    bool RadioOn { get; }
}

/// <summary>What a ship can ask a station.</summary>
public enum RadioRequest
{
    None,
    Laws,
    Docking,
    /// <summary>A docked ship asks to leave: the hangar's door opens and its pad lets it go.</summary>
    Launch,
}

/// <summary>A radio that takes what stations send (a ship's). Off, it hears nothing.</summary>
public interface IRadioReceiver : IRadio
{
    /// <summary>It hears a radio as far as that radio's range times this.</summary>
    float Sensitivity { get; }
    /// <summary>A station's laws, sent to it.</summary>
    void Receive(StationRadio from, StationLaws laws);
    /// <summary>A station clears it to a dock and a pad in it (their numbers; pad 0: a hangar with no pads; dock 0: the clearance is over).</summary>
    void Cleared(StationRadio from, int dock, int pad);
    /// <summary>
    /// A message: the words (shown), and how they are spoken (see
    /// <see cref="RadioVoicePack.Speech"/>; empty: only the squelch is heard).
    /// </summary>
    void Hear(IRadio from, string text, string speech);
    /// <summary>The radio of a ship another machine flies: what it hears goes there, and that machine's stations send it laws and clearances.</summary>
    bool Relayed => false;
}

public static class Radio
{
    /// <summary>The group every radio's node joins.</summary>
    public const string Group = "radios";
    public const int CallsignLength = 8;

    public static IEnumerable<IRadio> All(SceneTree tree)
    {
        foreach (Node node in tree.GetNodesInGroup(Group))
            if (node is IRadio radio && node is Node3D n && n.IsVisibleInTree())
                yield return radio;
    }

    /// <summary>The listener hears the radio: both on, and near enough.</summary>
    public static bool Hears(IRadioReceiver listener, IRadio radio) =>
        listener.RadioOn && radio.RadioOn &&
        listener.RadioPosition.DistanceTo(radio.RadioPosition) <= radio.RadioRange * listener.Sensitivity;

    /// <summary>The callsign as the radios send it: upper case, at most 8 characters; empty: the node's name.</summary>
    public static string Callsign(string given, Node fallback)
    {
        string s = string.IsNullOrWhiteSpace(given) ? fallback?.Name ?? "" : given;
        s = s.Trim().ToUpperInvariant();
        return s.Length > CallsignLength ? s[..CallsignLength] : s;
    }
}
