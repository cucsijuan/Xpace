using System;
using Godot;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// The generator's voice: while it charges a whine climbing to the point where
/// it takes hold; in cruise a deep hum of two beating tones under a rush of
/// the medium, rising with the ship's speed; when it lets go, a thump as the
/// veil comes back. Made here, sample by sample.
/// </summary>
public partial class CruiseHum : AudioStreamPlayer
{
    [Export] public NodePath ShipPath = "..";
    [Export] public float LowHz = 42f, HighHz = 95f;    // the hum, from standing to full speed
    [Export] public float BeatHz = 1.3f;                // how far apart its two tones are
    [Export] public float WhineLowHz = 180f, WhineHighHz = 900f;
    [Export] public float HumLevel = 0.2f, RushLevel = 0.05f, WhineLevel = 0.05f, ThumpLevel = 0.5f;
    [Export] public float FullSpeed = 4000f;            // m/s: where the hum is at its highest

    private ShipBody _ship;
    private CruiseDevice _cruise;
    private AudioStreamGeneratorPlayback _playback;
    private float _rate, _hz, _hum, _rush, _whine, _whineHz;
    private float _a, _b, _c, _low, _thump, _thumpPhase;
    private bool _wasOn;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _ship = GetNodeOrNull<ShipBody>(ShipPath);
        var generator = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.1f };
        _rate = generator.MixRate;
        _hz = LowHz;
        _whineHz = WhineLowHz;
        Stream = generator;
        Play();
        _playback = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        _cruise ??= _ship?.Device<CruiseDevice>();
        if (_playback == null || _cruise == null)
            return;
        float level = _ship.WakeLevel;
        float speed = Mathf.Clamp(_ship.LinearVelocity.Length() / FullSpeed, 0, 1);
        float charged = _cruise.Charging ? 1 - (float)(_cruise.ChargeLeft / Mathf.Max(_cruise.Spec.SpoolSeconds, 0.01)) : 0;
        if (_cruise.On != _wasOn)
        {
            if (_wasOn)
            {
                _thump = 1;
                _thumpPhase = 0;
            }
            _wasOn = _cruise.On;
        }
        float hz = LowHz + (HighHz - LowHz) * speed;
        float hum = HumLevel * level;
        float rush = RushLevel * level * (0.3f + speed);
        float whine = _cruise.Charging ? WhineLevel * (0.3f + charged) : 0;
        float whineHz = WhineLowHz + (WhineHighHz - WhineLowHz) * charged * charged;

        int frames = _playback.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            _hz += (hz - _hz) * 0.0005f;
            _hum += (hum - _hum) * 0.0007f;
            _rush += (rush - _rush) * 0.0007f;
            _whine += (whine - _whine) * 0.001f;
            _whineHz += (whineHz - _whineHz) * 0.002f;
            _a = (_a + _hz / _rate) % 1f;
            _b = (_b + (_hz + BeatHz) / _rate) % 1f;
            _c = (_c + _whineHz / _rate) % 1f;
            float tones = (2 * _a - 1) * 0.5f + (2 * _b - 1) * 0.5f;
            _low += (tones - _low) * 0.06f;             // the saw's top end off: a hum, not a buzz
            float noise = _rng.RandfRange(-1, 1);
            float sample = _low * _hum + noise * _rush + MathF.Sin(_c * MathF.Tau) * _whine;
            if (_thump > 0.001f)
            {
                _thumpPhase += 48f / _rate;
                sample += MathF.Sin(_thumpPhase * MathF.Tau) * _thump * ThumpLevel;
                _thump *= 0.99975f;
            }
            float s = Mathf.Clamp(sample, -1, 1);
            _playback.PushFrame(new Vector2(s, s));
        }
    }
}
