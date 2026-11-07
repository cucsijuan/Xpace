using Godot;
using Xpace.Computer;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// The little speaker on the ship's computer: the PC's own, driven by its timer. Any program can play on it
/// through ports 42h, 43h and 61h, as DeskPilot does when it starts.
/// </summary>
public partial class ComputerSpeaker : AudioStreamPlayer
{
    /// <summary>How loud, 0 to 1.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Level = 0.024f;
    /// <summary>How much of its edge a small cone takes off, 0 to 1.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Dullness = 0.6f;

    /// <summary>The tone it is playing now, or 0.</summary>
    public float Hz { get; private set; }

    private ShipBody _ship;
    private AudioStreamGeneratorPlayback _playback;
    private float _rate, _phase, _smooth;

    public override void _Ready()
    {
        _ship = ShipBody.Of(this);
        var generator = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.05f };
        _rate = generator.MixRate;
        Stream = generator;
        Play();
        _playback = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_playback == null || _ship?.Computer == null)     // (the ship finds its computer after its children are ready)
            return;
        float hz = Hz = (float)_ship.Computer.SpeakerHz;
        int frames = _playback.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            float sample = 0;
            if (hz > 0)
            {
                _phase = (_phase + hz / _rate) % 1f;
                sample = _phase < 0.5f ? Level : -Level;
            }
            _smooth += (sample - _smooth) * (1 - Dullness * 0.95f);
            _playback.PushFrame(new Vector2(_smooth, _smooth));
        }
    }
}
