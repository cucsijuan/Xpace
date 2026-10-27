using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A recorded radio voice (a station's operator): one clip per line, beside
/// its voice.tres in res://assets/voices/&lt;name&gt;/, each
/// &lt;line&gt;.wav (22050 Hz mono, imported uncompressed): "welcome",
/// "cleared", "hangar_1"...
///
/// What a radio says is spoken as a <see cref="Speech"/>: the voice's name,
/// a colon, and the clips one after another ("operator_f1:cleared hangar_3
/// follow_lights"). It travels as that string, so every machine speaks it
/// with its own copy of the clips; a clip this copy lacks is skipped.
/// </summary>
[GlobalClass]
public partial class RadioVoicePack : Resource
{
    public const string Folder = "res://assets/voices/";

    [Export] public string DisplayName = "";

    /// <summary>Its folder's name, which speeches use.</summary>
    public string Name => ResourcePath.GetBaseDir().GetFile();

    private readonly Dictionary<string, AudioStreamWav> _clips = new();
    private static readonly Dictionary<string, RadioVoicePack> Loaded = new();

    /// <summary>The clip for a line (null: none).</summary>
    public AudioStreamWav Clip(string line)
    {
        if (_clips.TryGetValue(line, out AudioStreamWav clip))
            return clip;
        string path = $"{ResourcePath.GetBaseDir()}/{line}.wav";
        clip = ResourceLoader.Exists(path) ? ResourceLoader.Load<AudioStreamWav>(path) : null;
        _clips[line] = clip;
        return clip;
    }

    /// <summary>The voice of that name (null: no such voice here).</summary>
    public static RadioVoicePack Find(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('/') || name.Contains('.'))
            return null;
        if (Loaded.TryGetValue(name, out RadioVoicePack voice))
            return voice;
        string path = $"{Folder}{name}/voice.tres";
        voice = ResourceLoader.Exists(path) ? ResourceLoader.Load<RadioVoicePack>(path) : null;
        Loaded[name] = voice;
        return voice;
    }

    /// <summary>A speech in this voice: the lines, {0}, {1}... filled in.</summary>
    public string Speech(string lines, params object[] args) =>
        string.IsNullOrWhiteSpace(lines) ? "" : $"{Name}:{string.Format(System.Globalization.CultureInfo.InvariantCulture, lines, args)}";

    /// <summary>A speech's voice and its lines (no voice: empty).</summary>
    public static (string Voice, string[] Lines) Parse(string speech)
    {
        int colon = (speech ?? "").IndexOf(':');
        if (colon <= 0)
            return ("", System.Array.Empty<string>());
        return (speech[..colon], speech[(colon + 1)..].Split(' ', System.StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>One of the voices, the same for a callsign on every machine.</summary>
    public static RadioVoicePack Pick(Godot.Collections.Array<RadioVoicePack> voices, string callsign)
    {
        if (voices == null || voices.Count == 0)
            return null;
        uint hash = 2166136261;                 // FNV-1a: string.GetHashCode differs between runs
        foreach (char c in callsign ?? "")
            hash = (hash ^ c) * 16777619;
        return voices[(int)(hash % (uint)voices.Count)];
    }
}
