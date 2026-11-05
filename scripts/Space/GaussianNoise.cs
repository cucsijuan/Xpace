using System;

namespace Xpace.Space;

/// <summary>
/// Noise with a bell curve for sensors, which draw it for every echo of every step: the engine's generator would cost a
/// call into it for each draw. The same seed gives the same noise.
/// </summary>
public sealed class GaussianNoise
{
    private readonly Random _random;
    private float _spare;
    private bool _hasSpare;

    public GaussianNoise(int seed) => _random = new Random(seed);

    public float Next(float mean = 0, float deviation = 1)
    {
        if (_hasSpare)
        {
            _hasSpare = false;
            return mean + deviation * _spare;
        }
        double radius = Math.Sqrt(-2 * Math.Log(1 - _random.NextDouble()));
        double angle = 2 * Math.PI * _random.NextDouble();
        _spare = (float)(radius * Math.Sin(angle));
        _hasSpare = true;
        return mean + deviation * (float)(radius * Math.Cos(angle));
    }
}
