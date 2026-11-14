using System;
using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>How a heat seeker's head tells what it follows from what else glows in its view.</summary>
public enum SeekerKind
{
    /// <summary>One detector: the centre of all the heat in its cone, each source weighed by how bright it is.</summary>
    Sum,
    /// <summary>
    /// Two detectors, a short band and a long: it learns the colour of what it first follows and weighs out what glows
    /// another colour (a flare burns far hotter than a plume). A target that changes colour, a motor cut or lit, can
    /// slip out of it too.
    /// </summary>
    TwoColour,
    /// <summary>
    /// A picture: it sees apart sources further apart than its resolution and follows the one that is where the target's
    /// way said it would be. A flare that falls behind is left; one still inside the resolution is a single blob with it.
    /// </summary>
    Imaging,
}

/// <summary>
/// A heat seeker's head: a detector on a gimbal that looks at a narrow
/// cone and follows what its kind (<see cref="SeekerKind"/>) takes for its
/// target. Tracking, the gimbal turns to keep that in the middle, as fast
/// as its motors go and no further than its stops. The same head rides on
/// the launcher's rail (the pilot hears it) and flies in the missile.
/// </summary>
public sealed class SeekerHead
{
    public SeekerKind Kind = SeekerKind.Sum;
    public float FieldDegrees = 8f;            // the whole cone it looks at
    public float Sensitivity = 1e-6f;          // the least heat it notices
    public float SlewDegrees = 60f;            // how fast the gimbal turns, per second
    public float GimbalLimitDegrees = 50f;     // how far off the nose it can look
    /// <summary>Two-colour: how far off the learned colour (ln of short over long) still counts as the target.</summary>
    public float ColourTolerance = 0.3f;
    /// <summary>Two-colour: the learned colour follows what it tracks over about this long, as a motor's glow changes.</summary>
    public float ColourSeconds = 1f;
    /// <summary>Imaging: sources closer than this (degrees) are one blob.</summary>
    public float ResolutionDegrees = 0.5f;
    /// <summary>Imaging: its blob growing brighter than this share of what it was means something joined it (a flare).</summary>
    public float JoinedShare = 1.4f;
    /// <summary>Imaging: how long it flies on the target's last turn while the blob is not the target alone.</summary>
    public float CoastSeconds = 1.5f;

    /// <summary>Where it points, world axes.</summary>
    public Vector3 Look = Vector3.Forward;
    /// <summary>The heat it follows at the last look.</summary>
    public float HeatSeen { get; private set; }
    /// <summary>The brightest thing in what it follows at the last look (null: nothing it notices).</summary>
    public IHeatSource Sees { get; private set; }
    /// <summary>It sees enough heat to follow.</summary>
    public bool Tone => HeatSeen >= Sensitivity;

    private readonly List<Glimpse> _view = new();
    private readonly List<Blob> _blobs = new();
    private bool _learned, _tracking;
    private float _colour;
    private Vector3 _track, _turn;              // imaging: where its target was, and how its direction turns (rad/s)
    private float _usual, _coasting;            // imaging: the target's blob as bright as it was; time flying on its way

    private struct Blob
    {
        public float Heat, Short, Best;
        public Vector3 Sum;
        public IHeatSource Brightest;
        public Vector3 Centre => Sum.Normalized();
    }

    /// <summary>Looks, and turns toward what it follows. False if it sees nothing to follow.</summary>
    public bool Track(Node where, Vector3 from, Vector3 nose, float dt)
    {
        Vector3 centre = Scan(where, from, dt);
        if (!Tone)
            return false;
        Vector3 look = Turned(centre, dt);
        if (look.AngleTo(nose) > Mathf.DegToRad(GimbalLimitDegrees))
            return false;                       // against the stops: it cannot look there
        Look = look;
        return true;
    }

    /// <summary>Turns toward a direction it is told (caged, or cued), as far as the stops allow, then looks.</summary>
    public void Point(Node where, Vector3 from, Vector3 nose, Vector3 to, float dt)
    {
        Vector3 look = Turned(to, dt);
        if (look.AngleTo(nose) <= Mathf.DegToRad(GimbalLimitDegrees))
            Look = look;
        Forget();                               // told where to look: what it followed is behind it
        Scan(where, from, dt);
    }

    /// <summary>Starts afresh: no colour learned, nothing followed.</summary>
    public void Forget()
    {
        _learned = false;
        _tracking = false;
        _turn = Vector3.Zero;
        _usual = 0;
        _coasting = 0;
    }

    private Vector3 Scan(Node where, Vector3 from, float dt)
    {
        Heat.Look(where, from, Look, Mathf.DegToRad(FieldDegrees) / 2, _view);
        Blob seen = Kind switch
        {
            SeekerKind.TwoColour => Coloured(dt),
            SeekerKind.Imaging => Imaged(dt),
            _ => All(),
        };
        HeatSeen = seen.Heat;
        Sees = HeatSeen >= Sensitivity ? seen.Brightest : null;
        return seen.Heat > 0 ? seen.Centre : Look;
    }

    private Blob All()
    {
        var all = new Blob();
        foreach (Glimpse g in _view)
            Add(ref all, g, g.Heat);
        return all;
    }

    private static void Add(ref Blob blob, Glimpse g, float weight)
    {
        blob.Heat += weight;
        blob.Short += weight * g.Share;
        blob.Sum += g.Direction * weight;
        if (weight > blob.Best)
        {
            blob.Best = weight;
            blob.Brightest = g.Source;
        }
    }

    // Each source weighed by how near its colour is to the one learned
    private Blob Coloured(float dt)
    {
        if (!_learned)
        {
            Blob all = All();
            if (all.Heat < Sensitivity)
                return all;
            _colour = Heat.Index(all.Heat, all.Short);
            _learned = true;
        }
        var seen = new Blob();
        foreach (Glimpse g in _view)
        {
            float off = (Heat.Index(1, g.Share) - _colour) / ColourTolerance;
            Add(ref seen, g, g.Heat * MathF.Exp(-0.5f * off * off));
        }
        if (seen.Heat >= Sensitivity)
            _colour += (Heat.Index(seen.Heat, seen.Short) - _colour) * MathF.Min(1, dt / MathF.Max(ColourSeconds, 1e-3f));
        return seen;
    }

    // The sources in blobs as far apart as it can tell them; the blob where its target should be
    private Blob Imaged(float dt)
    {
        float resolution = Mathf.DegToRad(ResolutionDegrees);
        _blobs.Clear();
        _view.Sort((a, b) => b.Heat.CompareTo(a.Heat));
        foreach (Glimpse g in _view)
        {
            int joined = -1;
            for (int i = 0; i < _blobs.Count && joined < 0; i++)
                if (_blobs[i].Centre.AngleTo(g.Direction) <= resolution)
                    joined = i;
            Blob blob = joined >= 0 ? _blobs[joined] : new Blob();
            Add(ref blob, g, g.Heat);
            if (joined >= 0)
                _blobs[joined] = blob;
            else
                _blobs.Add(blob);
        }
        Vector3 expected = _track;
        if (_tracking && _turn.LengthSquared() > 1e-12f)
            expected = _track.Rotated(_turn.Normalized(), _turn.Length() * dt);
        int pick = -1;
        float best = float.MaxValue;
        for (int i = 0; i < _blobs.Count; i++)
        {
            if (_blobs[i].Heat < Sensitivity)
                continue;
            // tracking: the nearest to where it should be, within a few cells; not yet: the brightest
            float score = _tracking ? _blobs[i].Centre.AngleTo(expected) : -_blobs[i].Heat;
            if (_tracking && score > 3 * resolution)
                continue;
            if (score < best)
            {
                best = score;
                pick = i;
            }
        }
        if (pick < 0)
        {
            if (_tracking)
                _track = expected.Normalized();     // nothing there: it keeps looking where the way goes
            return new Blob();
        }
        Blob picked = _blobs[pick];
        if (_tracking && _usual > 0 && picked.Heat > JoinedShare * _usual && _coasting < CoastSeconds)
        {
            // Something as bright joined its blob: the picture is no longer the target alone, its centre would go
            // halfway to the other. It looks where the target's way goes until they come apart.
            _coasting += dt;
            _track = expected.Normalized();
            picked.Sum = _track * picked.Heat;
            return picked;
        }
        Vector3 centre = picked.Centre;
        if (_tracking && dt > 0)
        {
            Vector3 axis = _track.Cross(centre);
            Vector3 turn = axis.LengthSquared() > 1e-14f ? axis.Normalized() * (_track.AngleTo(centre) / dt) : Vector3.Zero;
            _turn = _turn.Lerp(turn, 0.2f);
        }
        _usual = _usual > 0 && _coasting < CoastSeconds ? _usual + (picked.Heat - _usual) * 0.05f : picked.Heat;
        _coasting = 0;
        _track = centre;
        _tracking = true;
        return picked;
    }

    private Vector3 Turned(Vector3 to, float dt)
    {
        to = to.Normalized();
        float angle = Look.AngleTo(to);
        Vector3 axis = Look.Cross(to);
        if (angle < 1e-6f || axis.LengthSquared() < 1e-12f)
            return to;
        return Look.Rotated(axis.Normalized(), MathF.Min(angle, Mathf.DegToRad(SlewDegrees) * dt)).Normalized();
    }
}
