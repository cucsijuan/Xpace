using Godot;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// A radar track read as a pilot (or an AI) reads it: an alpha-beta filter
/// over the track's measured places, step by step, so the velocity is the
/// target's way and not the measurement's jitter (a far or jammed track can
/// jump a hundred metres a step). Starts again with every new lock, and a
/// measurement further from the prediction than any ship could be (a gate
/// that moved, a lock carried to something else) starts it again too.
/// One reading for every use of the track: lead, chase and interdiction.
/// </summary>
public sealed class TrackReading
{
    /// <summary>Share of the miss taken into the place each step.</summary>
    public float Alpha = 0.05f;
    /// <summary>Share of the miss (per step) taken into the velocity.</summary>
    public float Beta = 0.0013f;
    /// <summary>A miss beyond this (metres) is not the same target's way: start again.</summary>
    public float RestartMiss = 2000f;

    public Vector3 Position => _at.World;
    public Vector3 Velocity { get; private set; }
    /// <summary>Steps since this lock's reading started.</summary>
    public int Samples { get; private set; }
    /// <summary>Long enough to trust the velocity (about half a second).</summary>
    public bool Settled => Samples > 30;

    private Spot _at;
    private object _lock;

    /// <summary>Takes this step's measurement of the track of 'locked'; returns the velocity read.</summary>
    public Vector3 Take(Node owner, object locked, Vector3 measured, float dt)
    {
        if (locked == null || !ReferenceEquals(locked, _lock) || Samples == 0 || dt <= 0)
            return Start(owner, locked, measured);
        Vector3 predicted = _at.World + Velocity * dt;
        Vector3 miss = measured - predicted;
        if (miss.Length() > RestartMiss)
            return Start(owner, locked, measured);
        // The first steps weigh all measurements alike (a least-squares line through them), then the steady gains
        float k = Samples + 1;
        float alpha = Mathf.Max(Alpha, 2 * (2 * k - 1) / (k * (k + 1)));
        float beta = Mathf.Max(Beta, 6 / (k * (k + 1)));
        _at = Spot.At(owner, predicted + miss * alpha);
        Velocity += miss * (beta / dt);
        Samples++;
        return Velocity;
    }

    private Vector3 Start(Node owner, object locked, Vector3 measured)
    {
        _lock = locked;
        _at = Spot.At(owner, measured);
        Velocity = Vector3.Zero;
        Samples = locked == null ? 0 : 1;
        return Velocity;
    }
}
