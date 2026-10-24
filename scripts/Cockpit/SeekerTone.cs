using System;
using Godot;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// The heat seeker's growl in the pilot's headset: what the head on the
/// launcher's rail hears, made into sound the way a real seeker does it.
/// The spinning reticle chops the spot of heat into bursts of a carrier,
/// one burst per spoke pass, uneven from turn to turn; under it lies the
/// detector's own hiss, filtered around the carrier. Powered and seeing
/// nothing, mostly that low rasp; heat in view, a growl that rises and
/// grows louder with how much it sees; locked on, the head keeps the spot
/// centred, the bursts merge and a steadier tone is left. Silent while the
/// head is off: the fire control powers it on the first press of the fire
/// button with the heat seekers selected, and there is no head to power
/// once they are all gone.
/// </summary>
public partial class SeekerTone : AudioStreamPlayer
{
    [Export] public NodePath LauncherPath = "..";
    [Export] public float SearchHz = 100f;
    [Export] public float GrowlLowHz = 130f, GrowlHighHz = 240f;   // from the least heat it notices to 100 times that
    [Export] public float LockHz = 340f;
    [Export] public float SearchLevel = 0.015f, GrowlLowLevel = 0.035f, GrowlHighLevel = 0.05f, LockLevel = 0.035f;
    [Export] public float SpinHz = 32f;                            // reticle bursts a second: the growl's roughness
    [Export] public float SpinJitter = 0.2f;                       // how uneven the bursts are, in time and strength
    [Export] public float Drive = 2.5f;                            // headset amplifier overdrive: grit
    [Export] public float Dullness = 0.8f;                         // the headset's top end cut, 0 none to 1 all

    private LauncherDevice _launcher;
    private AudioStreamGeneratorPlayback _playback;
    private float _rate, _hz, _level, _depth, _noise;
    private float _phase, _spin, _spinHz, _burst = 1;
    private float _low, _band, _smooth;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _launcher = GetNodeOrNull<LauncherDevice>(LauncherPath);
        var generator = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.1f };
        _rate = generator.MixRate;
        _spinHz = SpinHz;
        Stream = generator;
        Play();
        _playback = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_playback == null || _launcher == null)
            return;
        (float hz, float level, float depth, float noise) = Wanted();
        int frames = _playback.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            // Glide to the new state, so changes do not click
            _hz += (hz - _hz) * 0.002f;
            _level += (level - _level) * 0.002f;
            _depth += (depth - _depth) * 0.002f;
            _noise += (noise - _noise) * 0.002f;

            // Carrier: a narrow pulse, buzzy with harmonics
            _phase = (_phase + _hz / _rate) % 1f;
            float carrier = _phase < 0.3f ? 1f : -0.43f;

            // Reticle turn: one burst per pass, each pass a little different
            _spin += _spinHz / _rate;
            if (_spin >= 1f)
            {
                _spin -= 1f;
                _spinHz = SpinHz * (1 + SpinJitter * _rng.RandfRange(-1, 1));
                _burst = 1 - SpinJitter * 2 * _rng.Randf();
            }
            float pass = MathF.Max(0, MathF.Sin(_spin * MathF.Tau));
            float envelope = Mathf.Lerp(1f, pass * pass * _burst, _depth);

            // Detector hiss through a band pass at the carrier
            float f = 2 * MathF.Sin(MathF.PI * Math.Min(_hz * 2, _rate / 6) / _rate);
            float high = _rng.RandfRange(-1, 1) - _low - _band;
            _band += f * high;
            _low += f * _band;
            float hiss = _band * (0.6f + 0.4f * envelope);

            // Amplifier: overdriven a little, then the headset's dull top end
            float raw = carrier * envelope * (1 - _noise) + hiss * _noise * 2;
            float driven = MathF.Tanh(raw * Drive) / MathF.Tanh(Drive);
            _smooth += (driven - _smooth) * (1 - Dullness * 0.95f);
            float sample = _smooth * _level;
            _playback.PushFrame(new Vector2(sample, sample));
        }
    }

    /// <summary>Pitch, level, how much the reticle chops it and how much hiss, for what the head hears now.</summary>
    private (float, float, float, float) Wanted()
    {
        if (!_launcher.HeadPowered)
            return (_hz, 0, _depth, _noise);
        if (_launcher.HeadLocked)
            return (LockHz, LockLevel, 0.15f, 0.05f);
        if (_launcher.HeatTone)
        {
            float strength = Math.Clamp(MathF.Log10(Math.Max(1, _launcher.HeadSignal)) / 2, 0, 1);
            return (Mathf.Lerp(GrowlLowHz, GrowlHighHz, strength),
                Mathf.Lerp(GrowlLowLevel, GrowlHighLevel, strength), 0.9f, Mathf.Lerp(0.4f, 0.15f, strength));
        }
        return (SearchHz, SearchLevel, 0.6f, 0.7f);
    }
}
