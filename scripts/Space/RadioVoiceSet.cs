using Godot;

namespace Xpace.Space;

/// <summary>
/// A list of recorded voices to pick from by callsign, the same pick on every machine. The pilots' set
/// (<see cref="Pilots"/>) gives every ship's commander its own voice, whichever station it talks to.
/// </summary>
[GlobalClass]
public partial class RadioVoiceSet : Resource
{
    public const string PilotsPath = "res://assets/voices/pilots.tres";

    [Export] public Godot.Collections.Array<RadioVoicePack> Voices = new();

    private static RadioVoiceSet _pilots;

    /// <summary>The ships' commanders' voices.</summary>
    public static RadioVoiceSet Pilots => _pilots ??= ResourceLoader.Exists(PilotsPath) ? ResourceLoader.Load<RadioVoiceSet>(PilotsPath) : new RadioVoiceSet();

    /// <summary>The voice for that callsign (null: the set is empty).</summary>
    public RadioVoicePack Pick(string callsign) => RadioVoicePack.Pick(Voices, callsign);
}
