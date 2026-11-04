using System.Globalization;
using System.Text;

namespace SkyCareer.Domain.Career;

/// <summary>
/// The airline's ICAO designator (three letters, e.g. "SCA"), its radiotelephony callsign ("SKY CAREER") and flight
/// callsigns ("SCA1234"). Both codes can be derived from the airline name when the player doesn't set them.
/// </summary>
public static class AirlineCodes
{
    public const int MaxFlightNumber = 9999;

    /// <summary>Words that don't make an airline distinctive and are skipped when deriving codes.</summary>
    private static readonly HashSet<string> Generic =
    [
        "AIR", "AIRLINE", "AIRLINES", "AIRWAYS", "AVIATION", "FLY", "FLIGHT", "FLIGHTS", "CARGO", "EXPRESS",
        "INTERNATIONAL", "GMBH", "AG", "LTD", "THE", "AND"
    ];

    /// <summary>
    /// Initials of the distinctive words, then of the generic ones ("Sky Career Air" → "SCA"), filled up with the first
    /// distinctive word's consonants and then its other letters ("Lufthansa" → "LFT").
    /// </summary>
    public static string IcaoFromName(string name)
    {
        var words = Words(name);
        var distinctive = words.Where(w => !Generic.Contains(w)).ToList();
        if (distinctive.Count == 0) distinctive = words;
        if (distinctive.Count == 0) return "XXX";

        var initials = distinctive.Concat(words.Where(w => !distinctive.Contains(w))).Take(3).Select(w => w[0]);
        var code = new StringBuilder(string.Concat(initials));
        var first = distinctive[0];
        foreach (char c in first.Skip(1).Where(c => !"AEIOU".Contains(c)).Concat(first.Skip(1).Where(c => "AEIOU".Contains(c))))
        {
            if (code.Length >= 3) break;
            code.Append(c);
        }
        while (code.Length < 3) code.Append('X');
        return code.ToString();
    }

    /// <summary>The distinctive words in capitals ("Sky Career Air" → "SKY CAREER").</summary>
    public static string TelephonyFromName(string name)
    {
        var words = Words(name);
        var distinctive = words.Where(w => !Generic.Contains(w)).ToList();
        var text = string.Join(' ', distinctive.Count > 0 ? distinctive : words);
        return text.Length > 30 ? text[..30].TrimEnd() : text;
    }

    public static bool IsValidIcao(string? code) => code is { Length: 3 } && code.All(c => c is >= 'A' and <= 'Z');

    public static bool IsValidTelephony(string? text) =>
        text is { Length: >= 2 and <= 30 } && text.All(c => c is >= 'A' and <= 'Z' or ' ') && text.Trim().Length >= 2;

    public static bool IsValidFlightNumber(int number) => number is >= 1 and <= MaxFlightNumber;

    /// <summary>ATC callsign of a flight: designator plus flight number ("SCA1234").</summary>
    public static string Callsign(string icao, int flightNumber) => $"{icao}{flightNumber}";

    /// <summary>Capital letters A–Z per word, accents removed ("Lüneburg" → "LUNEBURG").</summary>
    private static List<string> Words(string name)
    {
        var plain = new StringBuilder();
        foreach (char c in (name ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            char u = char.ToUpperInvariant(c);
            plain.Append(u is >= 'A' and <= 'Z' ? u : ' ');
        }
        return plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
