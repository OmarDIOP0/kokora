using System.Globalization;

namespace Kokora.Application.Common;

/// <summary>Heure locale (Africa/Dakar, GMT sans heure d'été) et formats de date en français.</summary>
public static class KokoraTime
{
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    public static readonly TimeZoneInfo Zone = FindZone();

    private static TimeZoneInfo FindZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Dakar"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; } // Dakar = UTC toute l'année
    }

    public static DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone);
    public static DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public static DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    /// <summary>Instant UTC du début de la journée locale.</summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>« 16h30 »</summary>
    public static string Hour(DateTimeOffset d)
    {
        var l = ToLocal(d);
        return l.Minute == 0 ? $"{l.Hour}h" : $"{l.Hour}h{l.Minute:00}";
    }

    /// <summary>« Samedi 12 septembre, 16h30 »</summary>
    public static string Long(DateTimeOffset d) => $"{Capitalize(ToLocal(d).ToString("dddd d MMMM", Fr))}, {Hour(d)}";

    /// <summary>« Samedi 12 septembre »</summary>
    public static string Day(DateOnly d) => Capitalize(d.ToString("dddd d MMMM", Fr));

    /// <summary>« Aujourd'hui », « Demain », « Hier » ou « Samedi 12 septembre ».</summary>
    public static string DayRelative(DateOnly d)
    {
        var diff = d.DayNumber - Today.DayNumber;
        return diff switch
        {
            0 => "Aujourd'hui",
            1 => "Demain",
            -1 => "Hier",
            _ => Day(d)
        };
    }

    /// <summary>« sam. » (jour abrégé, sans point final pour la bande de dates).</summary>
    public static string ShortDayName(DateOnly d) => d.ToString("ddd", Fr).TrimEnd('.');

    public static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Fr) + s[1..];
}
