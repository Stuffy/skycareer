using System.Globalization;
using SkyCareer.Domain;
using SkyCareer.Domain.World;
using SkyCareer.Localization;

namespace SkyCareer.Missions.Generation;

public enum DemandKind { TradeFair, Festival, SportsFinal, HolidayRush, ReliefSupplies, Harvest }

/// <summary>A region of the world an event applies to: a cell of about 90 × 90 nm.</summary>
public sealed record DemandRegion(int Row, int Column, double South, double West, double North, double East)
{
    public bool Contains(GeoPosition p) => p.Latitude >= South && p.Latitude < North && p.Longitude >= West && p.Longitude < East;
}

/// <summary>Something going on in a region for a game week that makes flights there more frequent and better paid.</summary>
/// <param name="Until">Start of the next game week (UTC), when the event is over.</param>
public sealed record DemandEvent(DemandKind Kind, decimal PayFactor, DateTime Until)
{
    /// <summary>The region the event applies to; every airport in it benefits.</summary>
    public DemandRegion? Region { get; init; }

    /// <summary>Passenger events raise scheduled and charter flights, the others cargo and bush supply.</summary>
    public bool ForPassengers => Kind is DemandKind.TradeFair or DemandKind.Festival or DemandKind.SportsFinal or DemandKind.HolidayRush;

    public bool AppliesTo(MissionType type) => ForPassengers
        ? type.Category is "airline" or "charter"
        : type.Category is "cargo" or "bush";

    public string Text => DemandEvents.KindText(Kind);
}

/// <summary>
/// Demand events of the game world: every game week (ISO week of the career's clock) some regions are in demand, and
/// every airport in such a region benefits. Pure and derived from the career's market seed, so the same career sees the
/// same events after a restart, like its market.
/// </summary>
public static class DemandEvents
{
    /// <summary>How much more likely a destination with a matching event is picked.</summary>
    public const double DestinationWeight = 4;
    public const decimal PassengerPayFactor = 1.25m, CargoPayFactor = 1.30m;
    /// <summary>Height of a region in degrees of latitude (1.5° = 90 nm); its width is the same distance at its latitude.</summary>
    public const double RegionDegrees = 1.5;
    /// <summary>Chance per week in per mille that a region has an event.</summary>
    public const int ChancePerMille = 50;

    /// <summary>The region a position lies in. Columns get wider in longitude towards the poles so regions stay about square.</summary>
    public static DemandRegion RegionOf(GeoPosition p)
    {
        int row = (int)Math.Floor((Math.Clamp(p.Latitude, -90, 89.999) + 90) / RegionDegrees);
        double south = row * RegionDegrees - 90;
        double width = Math.Min(360, RegionDegrees / Math.Max(0.2, Math.Cos((south + RegionDegrees / 2) * Math.PI / 180)));
        double lon = ((p.Longitude + 180) % 360 + 360) % 360;
        int column = (int)Math.Floor(lon / width);
        double west = column * width - 180;
        return new DemandRegion(row, column, south, west, south + RegionDegrees, Math.Min(180, west + width));
    }

    /// <summary>This week's event in the region of <paramref name="airport"/>; military fields take no part.</summary>
    public static DemandEvent? At(int seed, DateTime gameTime, Airport airport) =>
        airport.IsMilitary || airport.Size is AirportSize.Heliport or AirportSize.Seaplane ? null : At(seed, gameTime, airport.Position);

    public static DemandEvent? At(int seed, DateTime gameTime, GeoPosition position)
    {
        int year = ISOWeek.GetYear(gameTime), week = ISOWeek.GetWeekOfYear(gameTime);
        var region = RegionOf(position);
        if (StableHash.Of(seed, "demand", year, week, region.Row, region.Column) % 1000 >= ChancePerMille) return null;

        var kinds = Enum.GetValues<DemandKind>();
        var kind = kinds[StableHash.Of(seed, "kind", year, week, region.Row, region.Column) % kinds.Length];
        var until = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday).AddDays(7);
        return new DemandEvent(kind, PayFactorOf(kind), DateTime.SpecifyKind(until, DateTimeKind.Utc)) { Region = region };
    }

    public static decimal PayFactorOf(DemandKind kind) =>
        kind is DemandKind.ReliefSupplies or DemandKind.Harvest ? CargoPayFactor : PassengerPayFactor;

    /// <summary>"Trade fair +25 %" for a stored event; null for none.</summary>
    public static string? Label(string? stored) => Parse(stored) is { } kind ? $"{KindText(kind)} +{PayFactorOf(kind) - 1:P0}" : null;

    public static string KindText(DemandKind kind) => kind switch
    {
        DemandKind.TradeFair => L.T("Trade fair"),
        DemandKind.Festival => L.T("Festival"),
        DemandKind.SportsFinal => L.T("Sports final"),
        DemandKind.HolidayRush => L.T("Holiday rush"),
        DemandKind.ReliefSupplies => L.T("Relief supplies"),
        _ => L.T("Harvest season")
    };

    /// <summary>The event stored on a contract (<see cref="DemandKind"/> name); null for none or an unknown name.</summary>
    public static DemandKind? Parse(string? stored) => Enum.TryParse<DemandKind>(stored, out var kind) ? kind : null;
}
