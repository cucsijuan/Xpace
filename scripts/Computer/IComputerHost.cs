namespace Xpace.Computer;

/// <summary>Whatever a computer is the brain of (a ship, a station's terminal): its screens find the computer through it.</summary>
public interface IComputerHost
{
    ShipComputer Computer { get; }
}
