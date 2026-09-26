using System;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.AnimeThemes;

internal static class ThemeFilenameMatcher
{
    internal static bool Matches(string path, string sourceFilename, string? themeSlug, string extension, bool allowGenericThemeFilename)
    {
        var filename = Path.GetFileName(path);
        if (!filename.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(filename);
        return MatchesIdentity(stem, sourceFilename)
            || MatchesIdentity(stem, themeSlug)
            || (allowGenericThemeFilename && string.Equals(stem, "theme", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesIdentity(string stem, string? identity)
    {
        if (string.IsNullOrEmpty(identity))
        {
            return false;
        }

        if (string.Equals(stem, identity, StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith($"__{identity}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var marker = $"{identity}__";
        var markerIndex = stem.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || (markerIndex > 0 && !stem[..markerIndex].EndsWith("__", StringComparison.Ordinal)))
        {
            return false;
        }

        return IsVolumeSuffix(stem[(markerIndex + marker.Length)..]);
    }

    private static bool IsVolumeSuffix(string value)
    {
        return value.Length > 0
            && value.All(character => char.IsAsciiDigit(character) || character == '.' || character == ',');
    }
}
