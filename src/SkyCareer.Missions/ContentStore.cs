using System.Text.Json;
using SkyCareer.Domain.Aircraft;

namespace SkyCareer.Missions;

/// <summary>
/// Holds the current <see cref="GameContent"/>: shipped content plus the user's own folder layered on top.
/// User profiles are written to the user folder so app updates never overwrite them.
/// </summary>
public sealed class ContentStore
{
    private static readonly JsonSerializerOptions WriteJson = new(ContentLoader.Json) { WriteIndented = true };

    private readonly string _builtInRoot;

    public ContentStore(string builtInRoot, string userRoot)
    {
        _builtInRoot = builtInRoot;
        UserRoot = userRoot;
        Current = ContentLoader.Load(_builtInRoot, UserRoot);
    }

    /// <summary>For tests and tools: wraps fixed content without any files.</summary>
    public ContentStore(GameContent content)
    {
        _builtInRoot = UserRoot = "";
        Current = content;
    }

    public GameContent Current { get; private set; }
    public string UserRoot { get; }
    public string UserAircraftDir => Path.Combine(UserRoot, "aircraft");

    public event Action? Changed;

    public static string DefaultUserRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SkyCareer", "content");

    public void Reload()
    {
        if (_builtInRoot.Length == 0) return;
        Current = ContentLoader.Load(_builtInRoot, UserRoot);
        Changed?.Invoke();
    }

    /// <summary>True if the profile comes from (or overrides into) the user folder and can be edited/deleted there.</summary>
    public bool IsUserProfile(string id) =>
        UserRoot.Length > 0 && Current.AircraftFiles.TryGetValue(id, out var file) &&
        Path.GetFullPath(file).StartsWith(Path.GetFullPath(UserAircraftDir), StringComparison.OrdinalIgnoreCase);

    public async Task<string> SaveAircraftAsync(AircraftProfile profile, string? previousId = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(UserAircraftDir);
        var path = Path.Combine(UserAircraftDir, $"{profile.Id}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, WriteJson), ct);

        // Renaming an own profile: remove the old file so it doesn't come back as a second profile.
        if (previousId is not null && !previousId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase) && IsUserProfile(previousId))
            File.Delete(Current.AircraftFiles[previousId]);

        Reload();
        return path;
    }

    public void DeleteUserAircraft(string id)
    {
        if (!IsUserProfile(id)) throw new InvalidOperationException("Nur eigene Profile können gelöscht werden.");
        File.Delete(Current.AircraftFiles[id]);
        Reload();
    }

    public static string ToJson(AircraftProfile profile) => JsonSerializer.Serialize(profile, WriteJson);
}
