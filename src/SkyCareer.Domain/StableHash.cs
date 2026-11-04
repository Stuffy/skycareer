using System.Globalization;

namespace SkyCareer.Domain;

/// <summary>
/// FNV-1a over the invariant text of the parts: the same on every run and machine (unlike <c>string.GetHashCode</c>),
/// so seeds built from it give reproducible results across app restarts.
/// </summary>
public static class StableHash
{
    public static int Of(params object?[] parts)
    {
        uint hash = 2166136261;
        foreach (var part in parts)
        {
            foreach (char c in Convert.ToString(part, CultureInfo.InvariantCulture) ?? "") hash = (hash ^ c) * 16777619;
            hash = (hash ^ '|') * 16777619;
        }
        return (int)(hash & 0x7FFFFFFF);
    }
}
