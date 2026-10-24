using System;
using System.Collections.Generic;
using Godot;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// The warning receiver's audio in the pilot's headset, made by the
/// receiver itself: what it hears of a radar is the radar's own pulse rate
/// (<see cref="Space.IRadarEmitter.PrfHz"/>), played as a buzz.
///
///   a new radar       two short beeps
///   a search beam     a short burst of its pulses each time it sweeps past
///   a lock            its pulses without a break, for as long as it holds
///   a missile coming  a two-tone warble over everything else
///
/// <see cref="Volume"/> is the knob on the panel (0 to 1, heard as its
/// square, so the travel sounds even); the program can switch the sound
/// off through the receiver (<see cref="RwrDevice.AudioOn"/>).
/// </summary>
public partial class RwrTone : AudioStreamPlayer
{
    [Export] public NodePath RwrPath = "..";
    [Export] public float NewHz = 1000f;
    [Export] public float NewBeepSeconds = 0.07f;
    [Export] public float PingSeconds = 0.09f;
    [Export] public float MissileLowHz = 650f, MissileHighHz = 1250f;
    [Export] public float MissileWarbleHz = 4f;          // tone changes a second
    [Export] public float NewLevel = 0.04f, PingLevel = 0.04f, LockLevel = 0.035f, MissileLevel = 0.06f;
    [Export] public float PulseDuty = 0.3f;              // how much of each pulse period sounds
    [Export] public float Dullness = 0.6f;               // the headset's top end cut, 0 none to 1 all
    [Export(PropertyHint.Range, "0,1,0.01")] public float Volume { get; set; } = 0.59f;

    /// <summary>What it plays without a break now: "missile", "lock" or "" (for tests and the HUD).</summary>
    public string Sounding { get; private set; } = "";
    /// <summary>Short sounds played so far: new radars and beam passes.</summary>
    public int Beeps { get; private set; }

    private RwrDevice _rwr;
    private AudioStreamGeneratorPlayback _playback;
    private float _rate;
    private readonly Dictionary<RwrDevice.Contact, int> _pings = new();
    private readonly Queue<(float hz, int samples, float level)> _queue = new();
    private (float hz, int samples, float level) _sound;
    private float _phase, _steadyPhase, _warble, _steadyHz, _steadyLevel, _smooth;

    public override void _Ready()
    {
        _rwr = GetNodeOrNull<RwrDevice>(RwrPath);
        var generator = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.1f };
        _rate = generator.MixRate;
        Stream = generator;
        Play();
        _playback = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_rwr == null)
            return;
        Listen();
        if (_playback == null)
            return;
        int frames = _playback.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            if (_sound.samples <= 0 && _queue.Count > 0)
                _sound = _queue.Dequeue();
            float sample = 0;
            if (_sound.samples > 0)
            {
                _sound.samples--;
                _phase = (_phase + _sound.hz / _rate) % 1f;
                sample += Pulse(_phase) * _sound.level;
            }
            float hz = _steadyHz;
            if (Sounding == "missile")
            {
                _warble = (_warble + MissileWarbleHz / _rate) % 1f;
                hz = _warble < 0.5f ? MissileLowHz : MissileHighHz;
            }
            if (_steadyLevel > 0)
            {
                _steadyPhase = (_steadyPhase + hz / _rate) % 1f;
                sample += Pulse(_steadyPhase) * _steadyLevel;
            }
            _smooth += (sample - _smooth) * (1 - Dullness * 0.95f);
            float output = _rwr.AudioOn ? _smooth * Volume * Volume : 0;
            _playback.PushFrame(new Vector2(output, output));
        }
    }

    private float Pulse(float phase) => phase < PulseDuty ? 1f : -PulseDuty / (1 - PulseDuty);

    /// <summary>What changed in the receiver's table since the last frame, as sounds.</summary>
    private void Listen()
    {
        RwrDevice.Contact locked = null;
        bool missile = false;
        var seen = new HashSet<RwrDevice.Contact>();
        foreach (RwrDevice.Contact c in _rwr.Contacts)
        {
            seen.Add(c);
            if (c.IsMissile)
            {
                missile = true;
                continue;
            }
            if (c.Locked)
                locked ??= c;
            if (!_pings.TryGetValue(c, out int before))
            {
                Beep(NewHz, NewBeepSeconds, NewLevel);
                Beep(0, NewBeepSeconds * 0.8f, 0);
                Beep(NewHz, NewBeepSeconds, NewLevel);
            }
            else if (c.Pings > before && !c.Locked)
                Beep(c.Emitter.PrfHz, PingSeconds, PingLevel);
            _pings[c] = c.Pings;
        }
        foreach (RwrDevice.Contact gone in new List<RwrDevice.Contact>(_pings.Keys))
            if (!seen.Contains(gone))
                _pings.Remove(gone);

        if (missile)
        {
            Sounding = "missile";
            _steadyLevel = MissileLevel;
        }
        else if (locked != null)
        {
            Sounding = "lock";
            _steadyHz = locked.Emitter.PrfHz;
            _steadyLevel = LockLevel;
        }
        else
        {
            Sounding = "";
            _steadyLevel = 0;
        }
    }

    private void Beep(float hz, float seconds, float level)
    {
        if (level > 0)
            Beeps++;
        if (_queue.Count < 12)                  // (no audio out: nothing drains it)
            _queue.Enqueue((hz, (int)(seconds * _rate), level));
    }
}
