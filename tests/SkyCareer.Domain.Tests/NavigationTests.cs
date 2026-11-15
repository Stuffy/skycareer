using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.World;

namespace SkyCareer.Domain.Tests;

public class NavigationTests
{
    private static readonly GeoPosition Eddf = new(50.033333, 8.570556);
    private static readonly GeoPosition Egll = new(51.4775, -0.461389);

    [Fact]
    public void Initial_course_is_the_great_circle_course()
    {
        Assert.InRange(Eddf.InitialCourseTo(Egll), 286, 290);
        Assert.InRange(Egll.InitialCourseTo(Eddf), 100, 104);
        Assert.Equal(0, new GeoPosition(10, 20).InitialCourseTo(new GeoPosition(11, 20)), 6);
        Assert.Equal(270, new GeoPosition(0, 20).InitialCourseTo(new GeoPosition(0, 19)), 6);
    }

    [Fact]
    public void Direct_to_gives_magnetic_course_ete_and_track_offset()
    {
        var from = new GeoPosition(-10, -50);
        var to = new GeoPosition(-9, -50); // due north, 60 nm
        // 20° west variation: magnetic = true − (−20) = 020.
        var nav = Navigation.DirectTo(from, to, 120, magVar: -20, trackTrue: 355);
        Assert.Equal(0, nav.TrueCourse, 3);
        Assert.Equal(20, nav.MagneticCourse!.Value, 3);
        Assert.Equal("020°M", nav.CourseText);
        Assert.InRange(nav.DistanceNm, 59.9, 60.1);
        Assert.Equal(30, nav.Ete!.Value.TotalMinutes, 0);
        Assert.Equal(-5, nav.TrackOffset!.Value, 3);   // 355 is left of 360
        Assert.Equal(5, Navigation.DirectTo(from, to, 120, trackTrue: 5).TrackOffset!.Value, 3);
        Assert.Null(Navigation.DirectTo(from, to, 10).Ete); // too slow for a time en route
        Assert.Equal("360°T", Navigation.DirectTo(from, to, 0).CourseText);
    }

    [Fact]
    public void Coordinates_are_written_in_degrees_and_decimal_minutes()
    {
        Assert.Equal("N50°02.00' E008°34.23'", Navigation.DegreesMinutes(Eddf));
        Assert.Equal("S12°30.00' W045°15.00'", Navigation.DegreesMinutes(new GeoPosition(-12.5, -45.25)));
        // 59.9999' rounds up to the next degree.
        Assert.Equal("N11°00.00' E001°00.00'", Navigation.DegreesMinutes(new GeoPosition(10.9999999, 0.9999999)));
        // Decimal pair for the MSFS world map search.
        Assert.Equal("-18.49150, -51.29880", Navigation.Decimal(new GeoPosition(-18.4915, -51.2988)));
    }

    [Theory]
    [InlineData("EDDF", true)]
    [InlineData("1N7", true)]
    [InlineData("SSKW", true)]
    [InlineData("BR-2197", false)]
    [InlineData("US-1234", false)]
    [InlineData("CA0042", false)]
    public void Idents_that_are_no_codes_are_not_expected_in_the_sim(string ident, bool known)
    {
        var a = new Airport(ident, "Strip", new GeoPosition(0, 0), AirportSize.Small, 600, [RunwaySurface.Grass]);
        Assert.Equal(known, a.KnownToSim);
        Assert.True((a with { InSim = true }).KnownToSim);
        Assert.False((a with { InSim = false }).KnownToSim);
    }

    [Fact]
    public void Ete_text_and_degrees_read_like_a_pilot_would()
    {
        Assert.Equal("1:05", Navigation.EteText(TimeSpan.FromMinutes(65)));
        Assert.Equal("005", Navigation.Degrees(4.6));
        Assert.Equal("360", Navigation.Degrees(359.7));
    }
}
