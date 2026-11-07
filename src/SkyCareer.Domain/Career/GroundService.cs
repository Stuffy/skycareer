namespace SkyCareer.Domain.Career;

/// <summary>Optional GSX Pro services that earn reputation when completed before departure on a passenger flight.</summary>
public enum GroundService
{
    Catering,
    Cleaning,
    Water,
    Lavatory
}

public static class GroundServiceCodec
{
    /// <summary>Stored form on <see cref="MissionContract.GroundServices"/>: names separated by commas, null when empty.</summary>
    public static string? Serialize(IEnumerable<GroundService>? services)
    {
        var list = services?.Distinct().Order().ToList();
        return list is { Count: > 0 } ? string.Join(',', list) : null;
    }

    public static IReadOnlyList<GroundService> Parse(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<GroundService>(s, out var g) ? g : (GroundService?)null)
                .OfType<GroundService>().Distinct().ToList();
}
