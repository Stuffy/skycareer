using System.Runtime.CompilerServices;

namespace SkyCareer.Sim.Tests;

/// <summary>Tests assert on the English UI texts, whatever the machine's language.</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseEnglish() => SkyCareer.Localization.L.SetLanguage("en");
}
