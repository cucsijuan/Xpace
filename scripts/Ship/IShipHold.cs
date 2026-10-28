namespace Xpace.Ship;

/// <summary>
/// Something that holds a ship (a landing pad's clamps): while it is the
/// ship's <see cref="ShipBody.Holder"/>, it says where the ship is each
/// step instead of the thrusters and the integrator.
/// </summary>
public interface IShipHold
{
    /// <summary>Where the ship is after this step (its velocities those the hold gives it).</summary>
    ShipState Hold(ShipBody ship, double delta);
}
