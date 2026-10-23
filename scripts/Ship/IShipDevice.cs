namespace Xpace.Ship;

/// <summary>
/// A device on the ship's bus, below the ship in the scene. Each physics
/// step the ship calls <see cref="BeforeStep"/> on every device, runs the
/// computer, then calls <see cref="AfterStep"/>.
/// </summary>
public interface IShipDevice
{
    /// <summary>Put what the program will read into the registers (and memory windows).</summary>
    void BeforeStep(ShipBody ship, double delta);

    /// <summary>Act on what the program wrote.</summary>
    void AfterStep(ShipBody ship, double delta);
}

/// <summary>Where the devices with memory windows keep them: segment D000h, above the video memory.</summary>
public static class ShipMemory
{
    public const ushort Segment = 0xD000;
    public const int Base = Segment << 4;
    public const ushort Contacts = 0x1000, Track = 0x1100;               // the radar's picture
    public const ushort Screens = 0x1200;                                 // the display card's screen table; screens from 2000h
}
