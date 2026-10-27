using System;
using System.IO;
using System.Linq;
using Godot;
using Xpace.Cockpit;
using Xpace.Menu;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The operators' voices through the cockpit radio: every voice has every
/// line the stations speak (the hangars' numbers too), clips read as
/// samples, and a transmission is squelch, voice, squelch, loud but never
/// clipping past full scale, the radio's band cutting the deep end; it
/// plays on the Voices bus, whose volume is the settings' (Audio).
///
/// With XPACE_RADIO_PREVIEW set to a folder, each voice's welcome and a
/// clearance are written there as they sound in the game.
///
/// Run: Godot --headless --path . res://tests/radio_voice_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class RadioVoiceTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        var radio = new RadioVoice { Name = "Voice" };
        AddChild(radio);
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        var traffic = station.GetNode<StationRadio>("Radio");
        var security = station.GetNode<StationSecurity>("Security");
        string[] spoken =
        {
            traffic.WelcomeSpeech, traffic.NoDockSpeech, traffic.DeniedSpeech, traffic.ExpiredSpeech,
            security.WarningSpeech, security.EngageSpeech, security.DestroyedSpeech, security.FineSpeech,
        };
        string preview = System.Environment.GetEnvironmentVariable("XPACE_RADIO_PREVIEW");
        Check("the station has voices to pick from", traffic.Voices.Count >= 2);
        foreach (RadioVoicePack voice in traffic.Voices)
        {
            int clips = 0, missing = 0;
            foreach (string lines in spoken.Concat(Enumerable.Range(1, 20).Select(n => string.Format(traffic.ClearedSpeech, n))))
            {
                radio.Speak(voice.Speech(lines), out int c, out int m);
                clips += c;
                missing += m;
            }
            Check($"{voice.Name} ({voice.DisplayName}): every line ({clips} clips, {missing} missing)", missing == 0 && clips > 0);

            float[] sound = radio.Speak(voice.Speech(traffic.ClearedSpeech, 3), out int n, out _);
            int open = (int)(radio.SquelchSeconds * radio.Rate);
            float peak = sound.Max(MathF.Abs);
            float voiced = sound.Skip(open).Take(sound.Length - open * 2).Select(MathF.Abs).Average();
            Check($"  a clearance: {n} clips, {sound.Length / radio.Rate:0.0} s, peak {peak:0.00}, average {voiced:0.000}",
                  n == 3 && peak <= 1 && voiced > 0.05f);
            if (!string.IsNullOrEmpty(preview))
            {
                Directory.CreateDirectory(preview);
                float[] welcome = radio.Speak(voice.Speech(traffic.WelcomeSpeech), out _, out _);
                Write(Path.Combine(preview, $"{voice.Name}.wav"), welcome.Concat(new float[(int)radio.Rate / 2]).Concat(sound).ToArray(), (int)radio.Rate);
            }
        }

        // the band: a deep hum hardly passes, a voice's middle does
        Check($"the radio's band: 100 Hz {Gain(radio, 100):0.00}, 1500 Hz {Gain(radio, 1500):0.00}, 6000 Hz {Gain(radio, 6000):0.00}",
              Gain(radio, 100) < 0.3f * Gain(radio, 1500) && Gain(radio, 6000) < 0.5f * Gain(radio, 1500));

        int bus = AudioServer.GetBusIndex(GameSettings.VoicesBus);
        Check($"it plays on the Voices bus, at the settings' volume ({GameSettings.Current?.VoiceVolume:0.00})",
              radio.Bus == GameSettings.VoicesBus && bus > 0 && GameSettings.Current != null &&
              Mathf.Abs(Mathf.DbToLinear(AudioServer.GetBusVolumeDb(bus)) - GameSettings.Current.VoiceVolume) < 0.01f);

        station.Free();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static float Gain(RadioVoice radio, float hz)
    {
        var flutter = radio.Flutter;
        var crackles = radio.Crackles;
        var hiss = radio.Hiss;
        radio.Flutter = radio.Crackles = radio.Hiss = 0;
        var tone = new System.Collections.Generic.List<float>();
        for (int i = 0; i < (int)radio.Rate; i++)
            tone.Add(0.02f * Mathf.Sin(Mathf.Tau * hz * i / radio.Rate));   // quiet, under the compressor's hold
        float[] heard = radio.Transmission(tone);
        radio.Flutter = flutter;
        radio.Crackles = crackles;
        radio.Hiss = hiss;
        int open = (int)(radio.SquelchSeconds * radio.Rate);
        return heard.Skip(open + 2000).Take(tone.Count - 4000).Select(MathF.Abs).Average();
    }

    private static void Write(string path, float[] samples, int rate)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8);
        w.Write(36 + samples.Length * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples.Length * 2);
        foreach (float s in samples)
            w.Write((short)(Mathf.Clamp(s, -1, 1) * 32767));
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
