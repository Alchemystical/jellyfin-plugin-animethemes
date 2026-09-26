using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.AnimeThemes;

internal static class RootThemeLinkProjection
{
    internal const string Prefix = "shoko-season-theme__";

    internal static RootThemeLinkSynchronization Synchronize(
        string rootFolderPath,
        Guid sourceItemId,
        IEnumerable<string> sourcePaths,
        string themeDirectory,
        string extension)
    {
        var rootThemeDirectory = Path.Combine(rootFolderPath, themeDirectory);
        Directory.CreateDirectory(rootThemeDirectory);

        var sourcePrefix = $"{Prefix}{sourceItemId:N}__";
        var desiredPaths = sourcePaths
            .Where(File.Exists)
            .Select(path => (SourcePath: Path.GetFullPath(path), DestinationPath: Path.Combine(rootThemeDirectory, sourcePrefix + Path.GetFileName(path))))
            .ToArray();
        var desiredDestinationPaths = desiredPaths
            .Select(path => path.DestinationPath)
            .ToHashSet(StringComparer.Ordinal);

        bool changed = false;
        foreach (var path in Directory.EnumerateFiles(rootThemeDirectory, sourcePrefix + "*" + extension))
        {
            if (!desiredDestinationPaths.Contains(path) && IsSymbolicLink(path))
            {
                File.Delete(path);
                changed = true;
            }
        }

        var conflicts = new List<string>();
        foreach (var (sourcePath, destinationPath) in desiredPaths)
        {
            if (File.Exists(destinationPath) || IsSymbolicLink(destinationPath))
            {
                if (PointsTo(destinationPath, sourcePath))
                {
                    continue;
                }

                if (IsSymbolicLink(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                else
                {
                    conflicts.Add(destinationPath);
                    continue;
                }
            }

            File.CreateSymbolicLink(destinationPath, sourcePath);
            changed = true;
        }

        return new RootThemeLinkSynchronization(changed, conflicts);
    }

    internal static bool IsProjectionPath(string path)
    {
        return Path.GetFileName(path).StartsWith(Prefix, StringComparison.Ordinal);
    }

    private static bool IsSymbolicLink(string path)
    {
        return new FileInfo(path).LinkTarget is not null;
    }

    private static bool PointsTo(string linkPath, string sourcePath)
    {
        var linkTarget = new FileInfo(linkPath).LinkTarget;
        if (linkTarget is null)
        {
            return false;
        }

        var resolvedTarget = Path.IsPathFullyQualified(linkTarget)
            ? linkTarget
            : Path.Combine(Path.GetDirectoryName(linkPath)!, linkTarget);
        return string.Equals(Path.GetFullPath(resolvedTarget), sourcePath, StringComparison.Ordinal);
    }
}
