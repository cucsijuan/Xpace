using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Ship;

namespace Xpace.World;

/// <summary>What a crew does with a beam: it reads the frequency, and sets the generator to what it read a moment ago.</summary>
public sealed class CrewTuner
{
    private readonly Queue<(double At, byte Band)> _heard = new();
    private double _now;
    private float _tuneTo = 128;

    public float Hear(bool beam, byte band, float dt, double lag)
    {
        _now += dt;
        if (!beam)
        {
            _heard.Clear();
            return _tuneTo;
        }
        _heard.Enqueue((_now, band));
        while (_heard.Count > 0 && _now - _heard.Peek().At >= lag)
            _tuneTo = _heard.Dequeue().Band;
        return _tuneTo;
    }
}

/// <summary>
/// A ship on a record meets the medium and a beam with its <see cref="GeneratorState"/>, seen or not, and its crew works
/// it. What it loses comes off the record: it gets there that much later. Where it is shown the beam comes from the ship's
/// <see cref="CruiseDevice"/>; out of sight, from the fixed interdictors in the sector.
/// </summary>
public static class Interdiction
{
    private const float Substep = 1, Margin = 10;

    /// <summary>A step of a flying ship: how fast it goes now, given how fast its way would have it go.</summary>
    public static float Step(CargoShipRecord ship, ShipModel model, Voyage.Space space, Vector3 at, float wayspeed, float beam, byte band, float dt)
    {
        GeneratorSpec spec = model.Generator;
        GeneratorState generator = ship.Generator;
        float mass = model.Hull.Mass + ship.Tonnes * CargoHold.KilogramsPerTonne;
        float thrust = model.Engine?.RatedThrust ?? model.Hull.MainThrust;
        float normal = Voyage.TopAt(1e9f, model.Hull, mass, spec, thrust);
        if (ship.Pace < 0)
            ship.Pace = wayspeed;
        // Its way already has it out of cruise here, or nothing is troubling it
        if (generator.On && (beam <= 0 || wayspeed <= normal * 1.05f) && ship.Pace >= wayspeed)
        {
            ship.Pace = -1;
            ship.Crew.Hear(false, 0, dt, model.CrewTuneSeconds);
            generator.Strain = Mathf.Max(0, generator.Strain - dt / Mathf.Max(2 * spec.HoldSeconds, 0.01f));
            return wayspeed;
        }
        float tuneTo = ship.Crew.Hear(beam > 0, band, dt, model.CrewTuneSeconds);
        generator.Step(spec, Voyage.Density(space, at), beam, band, tuneTo, dt);
        if (!generator.On)
            generator.Start(spec);
        float most = Mathf.Min(wayspeed, generator.On ? Voyage.TopAt(generator.Density, model.Hull, mass, spec, thrust) : normal);
        ship.Pace = Mathf.MoveToward(ship.Pace, most, (ship.Pace > most ? spec.BrakeAcceleration : spec.Push / mass) * dt);
        float shown = Mathf.Min(ship.Pace, wayspeed);
        double behind = wayspeed > 0 ? dt * (1 - shown / wayspeed) : 0;
        ship.Left += behind;
        ship.Arrives += behind;
        return shown;
    }

    /// <summary>Brings a flying ship nobody is showing up to the clock, past whatever fixed interdictors are on its way.</summary>
    public static void Work(CargoShipRecord ship, ShipModel model, UniverseState state, StationBook book)
    {
        double from = ship.WorkedTo;
        ship.WorkedTo = state.Clock;
        if (state.Clock - ship.ShownAt < 2 || from <= 0 || book.Find(ship.From) is not StationBook.Station start || book.Find(ship.Going) is not StationBook.Station end)
            return;
        if (ship.Legs == null)
        {
            ship.Legs = book.Departure(start, StationBook.PadOf(start, ship.LeftDock, ship.LeftPad), end, model, ship.Tonnes, out _);
            ship.Beset = Window(ship, model, book);
        }
        // Mostly it is nowhere near one
        double along = from - ship.Left;
        if (ship.Generator.On && ship.Pace < 0 && (along < ship.Beset.From - Margin || along > ship.Beset.To + Margin))
            return;
        for (double t = from; t < state.Clock; t += Substep)
        {
            float dt = (float)System.Math.Min(Substep, state.Clock - t);
            if (book.Where(ship.Legs, model, ship.Tonnes, t - ship.Left) is not StationBook.Fix fix)
                continue;
            (float beam, byte band) = Beam(book.Space[fix.Sector], fix.At, t);
            Step(ship, model, book.Space[fix.Sector], fix.At, fix.Speed, beam, band, dt);
        }
    }

    // When on its way, in seconds out, a beam can reach it (none: an empty span)
    private static (double From, double To) Window(CargoShipRecord ship, ShipModel model, StationBook book)
    {
        if (!book.Beset(ship.Legs))
            return (double.MaxValue, double.MinValue);
        double from = double.MaxValue, to = double.MinValue, total = ship.Legs.Sum(l => l.Seconds + l.Transit);
        for (double t = 0; t < total; t += Margin)
            if (book.Where(ship.Legs, model, ship.Tonnes, t) is StationBook.Fix fix && Beam(book.Space[fix.Sector], fix.At, 0).Density > 0)
            {
                from = System.Math.Min(from, t);
                to = t;
            }
        return (from, to);
    }

    /// <summary>What the sector's fixed interdictors add at a point, and the frequency of the strongest.</summary>
    public static (float Density, byte Band) Beam(Voyage.Space space, Vector3 at, double clock)
    {
        float density = 0, strongest = 0;
        byte band = 0;
        foreach ((Vector3 place, InterdictorSpec spec) in space.Beams)
        {
            float adds = spec.Density(place.DistanceSquaredTo(at));
            density += adds;
            if (adds > strongest)
            {
                strongest = adds;
                band = spec.Band(clock);
            }
        }
        return (density, band);
    }
}
