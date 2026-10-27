using System;
using System.Collections.Generic;
using Godot;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Cockpit;

/// <summary>
/// The radio in the pilot's headset: each message the radio hears opens
/// the squelch (a burst of band-limited static), its speech is spoken (the
/// clips of the sender's recorded voice one after another, see
/// <see cref="RadioVoicePack"/>, through a cheap radio: a narrow band with
/// a nasal peak, squashed by its compressor, overdriven, coarse samples,
/// the level wavering and static crackling, hiss under it), and the
/// squelch closes. A message with no
/// speech, or in a voice this copy lacks, is only squelch: the words are on
/// the COMMS screen. Messages heard while one is spoken wait their turn.
///
/// Put it under the ship's <see cref="RadioDevice"/>.
/// </summary>
public partial class RadioVoice : AudioStreamPlayer
{
    [Export] public NodePath RadioPath = "..";
    [Export] public float SquelchSeconds = 0.18f;
    [Export] public float TailSeconds = 0.12f;
    [Export] public float StaticLevel = 0.015f;
    /// <summary>Silence between one clip and the next.</summary>
    [Export] public float GapSeconds = 0.12f;
    [Export] public float VoiceLevel = 0.3f;
    [ExportGroup("Radio")]
    /// <summary>The radio's band, Hz (each edge two poles).</summary>
    [Export] public float LowCut = 450f;
    [Export] public float HighCut = 2200f;
    /// <summary>The nasal peak: where (Hz), and how much of it is added.</summary>
    [Export] public float PeakHz = 1300f;
    [Export] public float Peak = 0.6f;
    /// <summary>The compressor: the level it holds, and how hard it holds it (0 none, 1 flat).</summary>
    [Export] public float CompressTo = 0.3f;
    [Export(PropertyHint.Range, "0,1")] public float Compress = 0.75f;
    /// <summary>Overdrive: 1 clean, more is a harder, cheaper radio.</summary>
    [Export] public float Drive = 6f;
    /// <summary>Bends the overdrive one way more than the other (0 even): a worn speaker's buzz.</summary>
    [Export] public float Asymmetry = 0.35f;
    /// <summary>Its samples: kept for this many (1 none, 2 half the rate), this many bits.</summary>
    [Export] public int Hold = 1;
    [Export] public int Bits = 9;
    /// <summary>The level wavers (0 none) about this fast, Hz.</summary>
    [Export(PropertyHint.Range, "0,1")] public float Flutter = 0.18f;
    [Export] public float FlutterHz = 5.5f;
    /// <summary>Crackles a second, and how loud.</summary>
    [Export] public float Crackles = 1.5f;
    [Export] public float CrackleLevel = 0.12f;
    /// <summary>The hiss under a voice, while the squelch is open.</summary>
    [Export] public float Hiss = 0.006f;

    /// <summary>Messages heard so far (spoken, or only squelched).</summary>
    public int Spoken { get; private set; }
    /// <summary>Clips spoken so far, and clips a speech named that this copy lacks.</summary>
    public int Clips { get; private set; }
    public int Missing { get; private set; }
    /// <summary>The squelch is open (a message is being heard).</summary>
    public bool Talking => _current != null || _queue.Count > 0;

    private RadioDevice _radio;
    private AudioStreamGeneratorPlayback _playback;
    private float _rate = 22050;
    private readonly Queue<float[]> _queue = new();
    private float[] _current;
    private int _at;
    private readonly RandomNumberGenerator _rng = new() { Seed = 77 };
    private float _noiseLow, _noiseBand;

    public override void _Ready()
    {
        _radio = GetNodeOrNull<RadioDevice>(RadioPath);
        if (_radio != null)
            _radio.MessageHeard += OnMessage;
        var generator = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.1f };
        _rate = generator.MixRate;
        Stream = generator;
        Bus = Menu.GameSettings.VoicesBus;      // its volume is the settings' (Audio: Radio voices)
        Play();
        _playback = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _ExitTree()
    {
        if (_radio != null)
            _radio.MessageHeard -= OnMessage;
    }

    private void OnMessage(string from, string text, string speech)
    {
        Spoken++;
        _queue.Enqueue(Speak(speech, out int clips, out int missing));
        Clips += clips;
        Missing += missing;
    }

    /// <summary>The squelch opening, the voice through the radio, the squelch closing.</summary>
    public float[] Transmission(List<float> voice)
    {
        int open = (int)(SquelchSeconds * _rate), tail = voice.Count > 0 ? (int)(TailSeconds * _rate) : 0;
        var output = new float[open + voice.Count + tail];
        // one-pole high- and low-passes, two of each: the radio's band
        float hp = Mathf.Exp(-Mathf.Tau * LowCut / _rate), lp = 1 - Mathf.Exp(-Mathf.Tau * HighCut / _rate);
        float hpIn1 = 0, hpOut1 = 0, hpIn2 = 0, hpOut2 = 0, low1 = 0, low2 = 0;
        // a state-variable band-pass: the nasal peak
        float f = 2 * Mathf.Sin(Mathf.Pi * PeakHz / _rate), q = 0.7f, svLow = 0, svBand = 0;
        float envelope = 0, attack = 1 - Mathf.Exp(-1 / (0.005f * _rate)), release = 1 - Mathf.Exp(-1 / (0.12f * _rate));
        // normalised by its stronger side; the bend leaves a little DC, blocked after it
        float bias = Asymmetry, norm = Math.Max(MathF.Tanh(Drive + bias) - MathF.Tanh(bias), MathF.Tanh(bias) - MathF.Tanh(bias - Drive));
        float dcIn = 0, dcOut = 0, dc = Mathf.Exp(-Mathf.Tau * 30 / _rate), steps = Mathf.Pow(2, Math.Max(1, Bits) - 1), held = 0;
        float phase = _rng.Randf() * Mathf.Tau, wander = 0, crackle = 0;
        for (int i = 0; i < output.Length; i++)
        {
            float noise = Static();
            if (i < open || i >= open + voice.Count)
            {
                output[i] = noise * StaticLevel * 6;
                continue;
            }
            float x = voice[i - open];
            hpOut1 = hp * (hpOut1 + x - hpIn1);
            hpIn1 = x;
            hpOut2 = hp * (hpOut2 + hpOut1 - hpIn2);
            hpIn2 = hpOut1;
            svLow += f * svBand;
            svBand += f * (hpOut2 - svLow - q * svBand);
            float y = hpOut2 + svBand * Peak;
            low1 += (y - low1) * lp;
            low2 += (low1 - low2) * lp;
            y = low2;
            float level = MathF.Abs(y);
            envelope += (level - envelope) * (level > envelope ? attack : release);
            y *= Math.Min(Mathf.Pow(CompressTo / Math.Max(envelope, 1e-4f), Compress), 6);   // silences stay quiet
            y = (MathF.Tanh(y * Drive + bias) - MathF.Tanh(bias)) / norm;
            dcOut = dc * (dcOut + y - dcIn);
            dcIn = y;
            y = dcOut;
            if (Hold <= 1 || (i - open) % Hold == 0)
                held = Mathf.Round(y * steps) / steps;
            y = held;
            // the level wavers: a slow beat and a slower wander
            wander += ((_rng.Randf() * 2 - 1) - wander) * (2f / _rate);
            phase += Mathf.Tau * FlutterHz / _rate;
            y *= Math.Max(0.2f, 1 - Flutter * (0.5f + 0.35f * Mathf.Sin(phase) + 1.2f * wander));
            if (_rng.Randf() < Crackles / _rate)
                crackle = CrackleLevel * (_rng.Randf() * 2 - 1);
            y += crackle + noise * Hiss * 6;
            crackle *= 0.6f;
            output[i] = Mathf.Clamp(y * VoiceLevel, -1, 1);
        }
        return output;
    }

    /// <summary>A speech as a transmission (also how previews are made): the samples at <see cref="Rate"/>.</summary>
    public float[] Speak(string speech, out int clips, out int missing)
    {
        clips = missing = 0;
        var voice = new List<float>();
        var (name, lines) = RadioVoicePack.Parse(speech);
        RadioVoicePack pack = RadioVoicePack.Find(name);
        foreach (string line in lines)
        {
            float[] clip = pack == null ? null : Samples(pack.Clip(line));
            if (clip == null)
            {
                missing++;
                continue;
            }
            clips++;
            if (voice.Count > 0)
                voice.AddRange(new float[(int)(GapSeconds * _rate)]);
            voice.AddRange(clip);
        }
        return Transmission(voice);
    }

    /// <summary>The radio's sample rate.</summary>
    public float Rate => _rate;

    // White noise through a crude band-pass: a radio's static, not a hiss
    private float Static()
    {
        float white = _rng.Randf() * 2 - 1;
        _noiseLow += (white - _noiseLow) * 0.35f;
        _noiseBand += (_noiseLow - _noiseBand) * 0.05f;
        return _noiseLow - _noiseBand;
    }

    /// <summary>A clip as samples at the radio's rate, mono (null: none, or not plain 16-bit PCM).</summary>
    private float[] Samples(AudioStreamWav clip)
    {
        if (clip == null || clip.Format != AudioStreamWav.FormatEnum.Format16Bits)
            return null;
        byte[] data = clip.Data;
        int channels = clip.Stereo ? 2 : 1, frames = data.Length / (2 * channels);
        double step = clip.MixRate / (double)_rate;
        var output = new float[(int)(frames / step)];
        for (int i = 0; i < output.Length; i++)
        {
            double at = i * step;
            int a = (int)at, b = Math.Min(a + 1, frames - 1);
            float t = (float)(at - a);
            output[i] = Mathf.Lerp(Frame(data, a, channels), Frame(data, b, channels), t);
        }
        return output;
    }

    private static float Frame(byte[] data, int frame, int channels)
    {
        float sum = 0;
        for (int c = 0; c < channels; c++)
            sum += BitConverter.ToInt16(data, (frame * channels + c) * 2) / 32768f;
        return sum / channels;
    }

    public override void _Process(double delta)
    {
        if (_playback == null)
            return;
        int frames = _playback.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            if (_current == null && _queue.Count > 0)
            {
                _current = _queue.Dequeue();
                _at = 0;
            }
            float sample = 0;
            if (_current != null)
            {
                sample = _current[_at++];
                if (_at >= _current.Length)
                    _current = null;
            }
            _playback.PushFrame(new Vector2(sample, sample));
        }
    }
}
