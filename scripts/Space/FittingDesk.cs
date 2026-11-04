using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Ship;
using Xpace.World;

namespace Xpace.Space;

/// <summary>What a pilot asks a station's fitters (<see cref="FittingDesk"/>), as JSON on the "fitting" topic.</summary>
public sealed class FittingRequest
{
    public const string Look = "look", Fit = "fit", Remove = "remove";

    /// <summary>The station, by its callsign.</summary>
    public string Station { get; set; }
    public string Do { get; set; } = Look;
    /// <summary>The hardpoint, by its name.</summary>
    public string Place { get; set; }
    /// <summary>To fit: the piece's place in the pilot's locker here.</summary>
    public int Index { get; set; }
}

/// <summary>What the fitters show a pilot: the ship's hardpoints and what is on them, and the pieces in their locker here.</summary>
public sealed class FittingView
{
    public string Station { get; set; }
    public bool Docked { get; set; }
    /// <summary>The hull's spec (its resource path): the screen shows its exterior.</summary>
    public string Hull { get; set; }
    public List<FittingPlace> Places { get; set; } = new();
    public List<FittingPiece> Locker { get; set; } = new();
    public string Message { get; set; } = "";
}

public sealed class FittingPlace
{
    public string Name { get; set; }
    public int Size { get; set; }
    public MountKinds Accepts { get; set; }
    /// <summary>What is on it (its resource path; empty: nothing), and its model's name.</summary>
    public string Mounted { get; set; } = "";
    public string Model { get; set; } = "";
}

public sealed class FittingPiece
{
    /// <summary>Its place in the locker.</summary>
    public int Index { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    /// <summary>The hardpoints it fits, by name.</summary>
    public List<string> FitsOn { get; set; } = new();
}

/// <summary>
/// A station's fitters, at its arms dealer for now (a retrofit hangar later): with the ship docked here, a pilot
/// takes a piece off a hardpoint into their locker here (<see cref="StationRecord.Lockers"/>), or fits one from the
/// locker (what was on that hardpoint goes to the locker). A hardpoint takes a piece of its size or smaller, of the
/// kinds it accepts (<see cref="Hardpoint.Fits"/>); a ship has one turret, and keeps rails for the missiles it has
/// aboard (a pylon carrying them can't come off). The pilot's record keeps what is fitted
/// (<see cref="ShipRecord.Fittings"/>), and the ship follows it (<see cref="PilotShip"/>, which restarts the ship's
/// computer so its programs see the new fit). Only the universe's owner does it, like the market.
/// </summary>
public static class FittingDesk
{
    public const string Topic = "fitting";


    /// <summary>Does what the pilot asks, if it may be done, and shows the ship's fit and the locker after.</summary>
    public static FittingView Serve(StationMarket market, string pilotId, FittingRequest request)
    {
        Universe universe = Universe.Current;
        PilotRecord pilot = universe.State.Pilot(pilotId);
        List<ItemRecord> locker = universe.State.Station(market.Radio.Sign).LockerOf(pilotId);
        Node3D ship = universe.ShipOf(pilotId);
        bool docked = market.Radio.DockedHere(ship);
        ShipSpec hull = ResourceLoader.Exists(pilot.Ship.Hull) ? GD.Load<ShipSpec>(pilot.Ship.Hull) : null;
        HardpointPlace[] places = Fittings.Places(hull);
        Dictionary<string, Resource> fitted = Fittings.Of(hull, pilot.Ship);
        HardpointPlace place = places.FirstOrDefault(p => p.Name == request.Place);
        string message = "";
        if (request.Do is FittingRequest.Fit or FittingRequest.Remove)
        {
            if (!docked)
                message = market.RefitDockText;
            else if (market.Radio.Security?.Refuses(ship) == true)
                message = market.RefusedText;
            else if (place == null)
                message = market.NoSuchText;
            else if (request.Do == FittingRequest.Remove)
            {
                if (fitted.GetValueOrDefault(place.Name) is not Outfit old)
                    message = market.EmptyText;
                else if (RailsWith(fitted, place.Name, null) < Universe.MissilesAboard(pilot))
                    message = string.Format(market.RailsFullText, Universe.MissilesAboard(pilot), RailsWith(fitted, place.Name, null));
                else
                {
                    locker.Add(ItemRecord.PieceOf(old));
                    pilot.Ship.Fittings[place.Name] = "";
                    message = string.Format(market.RemovedText, old.Model, place.Name);
                    universe.Touch(pilot);
                }
            }
            else if (request.Index < 0 || request.Index >= locker.Count || PieceIn(locker[request.Index]) is not Outfit piece)
                message = market.NothingText;
            else if (!Hardpoint.Fits(piece, place.Size, place.Accepts))
                message = market.NotFitText;
            else if (piece is TurretSpec && fitted.Any(f => f.Key != place.Name && f.Value is TurretSpec))
                message = market.OneTurretText;
            else if (RailsWith(fitted, place.Name, piece) < Universe.MissilesAboard(pilot))
                message = string.Format(market.RailsFullText, Universe.MissilesAboard(pilot), RailsWith(fitted, place.Name, piece));
            else
            {
                locker.RemoveAt(request.Index);
                if (fitted.GetValueOrDefault(place.Name) is Outfit old)
                    locker.Add(ItemRecord.PieceOf(old));
                pilot.Ship.Fittings[place.Name] = piece.ResourcePath;
                message = string.Format(market.FittedText, piece.Model, place.Name);
                universe.Touch(pilot);
            }
            fitted = Fittings.Of(hull, pilot.Ship);
        }

        var view = new FittingView { Station = market.Radio.Sign, Docked = docked, Hull = pilot.Ship.Hull, Message = message };
        foreach (HardpointPlace p in places)
        {
            Outfit on = fitted.GetValueOrDefault(p.Name) as Outfit;
            view.Places.Add(new FittingPlace { Name = p.Name, Size = p.Size, Accepts = p.Accepts, Mounted = on?.ResourcePath ?? "", Model = on?.Model ?? "" });
        }
        for (int i = 0; i < locker.Count; i++)
            if (PieceIn(locker[i]) is Outfit piece)
                view.Locker.Add(new FittingPiece
                {
                    Index = i, Name = piece.Model, Path = piece.ResourcePath,
                    FitsOn = places.Where(p => Hardpoint.Fits(piece, p.Size, p.Accepts)).Select(p => p.Name).ToList(),
                });
        return view;
    }

    // The missile rails the ship would have with this piece (or none) in place of what is on that hardpoint
    private static int RailsWith(Dictionary<string, Resource> fitted, string place, Resource piece)
    {
        var after = new Dictionary<string, Resource>(fitted) { [place] = piece };
        return after.Values.OfType<PylonSpec>().Sum(p => p.Rails);
    }

    // The model a locker item is, if it is a piece of equipment
    private static Outfit PieceIn(ItemRecord item) =>
        item.Kind == ItemRecord.Piece && ResourceLoader.Exists(item.Data) && GD.Load<Resource>(item.Data) is Outfit model && Fittings.IsPiece(model)
            ? model : null;
}
