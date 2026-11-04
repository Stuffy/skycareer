using SkyCareer.Domain.Career;

namespace SkyCareer.Domain.Tests;

public class AirlineCodesTests
{
    [Theory]
    [InlineData("Sky Career Air", "SCA", "SKY CAREER")]
    [InlineData("Lufthansa", "LFT", "LUFTHANSA")]
    [InlineData("Test Air", "TAS", "TEST")]
    [InlineData("Lüneburg Express", "LEN", "LUNEBURG")]
    [InlineData("Air Airlines", "AAR", "AIR AIRLINES")]
    [InlineData("Nordic Wings International", "NWI", "NORDIC WINGS")]
    [InlineData("", "XXX", "")]
    public void Codes_are_derived_from_the_name(string name, string icao, string telephony)
    {
        Assert.Equal(icao, AirlineCodes.IcaoFromName(name));
        Assert.Equal(telephony, AirlineCodes.TelephonyFromName(name));
    }

    [Fact]
    public void Codes_are_validated()
    {
        Assert.True(AirlineCodes.IsValidIcao("SCA"));
        Assert.False(AirlineCodes.IsValidIcao("SC"));
        Assert.False(AirlineCodes.IsValidIcao("sca"));
        Assert.False(AirlineCodes.IsValidIcao("SC1"));
        Assert.True(AirlineCodes.IsValidTelephony("SKY CAREER"));
        Assert.False(AirlineCodes.IsValidTelephony("S"));
        Assert.False(AirlineCodes.IsValidTelephony("SKY-CAREER"));
        Assert.True(AirlineCodes.IsValidFlightNumber(1));
        Assert.False(AirlineCodes.IsValidFlightNumber(10000));
        Assert.Equal("SCA1234", AirlineCodes.Callsign("SCA", 1234));
    }
}
