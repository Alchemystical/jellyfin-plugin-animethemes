using System;
using System.IO;
using Xunit;

namespace Jellyfin.Plugin.AnimeThemes.Tests;

public sealed class RootThemeLinkProjectionTests
{
    [Fact]
    public void SynchronizeCreatesAndRemovesSeasonThemeLink()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var rootFolder = Path.Combine(testRoot, "group");
        var seasonFolder = Path.Combine(testRoot, "season");
        var sourcePath = Path.Combine(seasonFolder, "theme-music", "4770__OshiNoKoS3-OP1-NCBD1080__50.mp3");
        var sourceId = Guid.NewGuid();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, "theme");

            var created = RootThemeLinkProjection.Synchronize(rootFolder, sourceId, [sourcePath], "theme-music", ".mp3");
            var linkPath = Path.Combine(rootFolder, "theme-music", $"shoko-season-theme__{sourceId:N}__{Path.GetFileName(sourcePath)}");

            Assert.True(created.Changed);
            Assert.Empty(created.Conflicts);
            Assert.Equal("theme", File.ReadAllText(linkPath));
            Assert.NotNull(new FileInfo(linkPath).LinkTarget);

            var unchanged = RootThemeLinkProjection.Synchronize(rootFolder, sourceId, [sourcePath], "theme-music", ".mp3");

            Assert.False(unchanged.Changed);

            var removed = RootThemeLinkProjection.Synchronize(rootFolder, sourceId, [], "theme-music", ".mp3");

            Assert.True(removed.Changed);
            Assert.False(File.Exists(linkPath));
        }
        finally
        {
            Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public void SynchronizePreservesNonLinkConflict()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var rootFolder = Path.Combine(testRoot, "group");
        var seasonFolder = Path.Combine(testRoot, "season");
        var sourcePath = Path.Combine(seasonFolder, "theme-music", "op1__50.mp3");
        var sourceId = Guid.NewGuid();
        var destinationPath = Path.Combine(rootFolder, "theme-music", $"shoko-season-theme__{sourceId:N}__{Path.GetFileName(sourcePath)}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(sourcePath, "season");
            File.WriteAllText(destinationPath, "root");

            var result = RootThemeLinkProjection.Synchronize(rootFolder, sourceId, [sourcePath], "theme-music", ".mp3");

            Assert.False(result.Changed);
            Assert.Equal([destinationPath], result.Conflicts);
            Assert.Equal("root", File.ReadAllText(destinationPath));
        }
        finally
        {
            Directory.Delete(testRoot, true);
        }
    }
}
