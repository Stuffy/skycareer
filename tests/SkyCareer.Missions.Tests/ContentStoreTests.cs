using SkyCareer.Domain.Aircraft;

namespace SkyCareer.Missions.Tests;

public sealed class ContentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"skyc-content-{Guid.NewGuid():N}");
    private readonly string _builtIn, _user;

    public ContentStoreTests()
    {
        _builtIn = Path.Combine(_root, "builtin");
        _user = Path.Combine(_root, "user");
        Directory.CreateDirectory(Path.Combine(_builtIn, "aircraft"));
        File.WriteAllText(Path.Combine(_builtIn, "aircraft", "c172.json"), ContentStore.ToJson(Plane("c172", "Cessna 172")));
    }

    public void Dispose() => Directory.Delete(_root, true);

    private static AircraftProfile Plane(string id, string name) => new()
    {
        Id = id, Name = name, IcaoType = "C172", OperatingEmptyKg = 767, MaxZeroFuelKg = 1111, MaxTakeoffKg = 1111,
        MaxLandingKg = 1111, MaxFuelKg = 145, CruiseKtas = 120, FuelBurnKgPerHour = 23
    };

    [Fact]
    public async Task Saved_profiles_are_loaded_and_override_built_in_ones()
    {
        var store = new ContentStore(_builtIn, _user);
        Assert.False(store.IsUserProfile("c172"));

        int changes = 0;
        store.Changed += () => changes++;
        await store.SaveAircraftAsync(Plane("c750", "Citation X"));
        await store.SaveAircraftAsync(Plane("c172", "Meine C172"), previousId: "c172");

        Assert.Equal(2, changes);
        Assert.Equal(["Citation X", "Meine C172"], store.Current.Aircraft.Select(a => a.Name));
        Assert.True(store.IsUserProfile("c172"));
        Assert.Empty(store.Current.Errors);

        // Deleting the override brings the shipped profile back.
        store.DeleteUserAircraft("c172");
        Assert.Equal("Cessna 172", store.Current.Aircraft.Single(a => a.Id == "c172").Name);
        Assert.Throws<InvalidOperationException>(() => store.DeleteUserAircraft("c172"));
    }

    [Fact]
    public async Task Renaming_an_own_profile_removes_the_old_file()
    {
        var store = new ContentStore(_builtIn, _user);
        await store.SaveAircraftAsync(Plane("old_id", "Plane"));
        await store.SaveAircraftAsync(Plane("new_id", "Plane"), previousId: "old_id");

        Assert.DoesNotContain(store.Current.Aircraft, a => a.Id == "old_id");
        Assert.Contains(store.Current.Aircraft, a => a.Id == "new_id");
        Assert.False(File.Exists(Path.Combine(_user, "aircraft", "old_id.json")));
    }

    [Fact]
    public void Shipped_profiles_have_no_validation_errors()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        var content = ContentLoader.Load(Path.Combine(dir!.FullName, "content"));

        foreach (var p in content.Aircraft)
            Assert.True(AircraftProfileValidator.IsValid(p),
                $"{p.Id}: {string.Join("; ", AircraftProfileValidator.Validate(p).Where(i => i.Level == IssueLevel.Error).Select(i => i.Message))}");
    }
}
