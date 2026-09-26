using Xunit;

namespace Jellyfin.Plugin.AnimeThemes.Tests;

public sealed class ThemeFilenameMatcherTests
{
    [Theory]
    [InlineData("OshiNoKoS3-OP1-NCBD1080__0.mp3")]
    [InlineData("OshiNoKoS3-OP1-NCBD1080__50.mp3")]
    [InlineData("OshiNoKoS3-OP1-NCBD1080__100.0.mp3")]
    [InlineData("4770__OshiNoKoS3-OP1-NCBD1080__25,5.mp3")]
    [InlineData("older-prefix__4770__OshiNoKoS3-OP1-NCBD1080__75.mp3")]
    public void MatchesCurrentAndPrefixedNamesRegardlessOfVolume(string filename)
    {
        Assert.True(ThemeFilenameMatcher.Matches(filename, "OshiNoKoS3-OP1-NCBD1080", "op1", ".mp3", false));
    }

    [Fact]
    public void MatchesHistoricSlugName()
    {
        Assert.True(ThemeFilenameMatcher.Matches("op1.mp3", "OshiNoKoS3-OP1-NCBD1080", "op1", ".mp3", false));
    }

    [Fact]
    public void MatchesHistoricGenericThemeOnlyWhenUnambiguous()
    {
        Assert.True(ThemeFilenameMatcher.Matches("theme.mp3", "OshiNoKoS3-OP1-NCBD1080", "op1", ".mp3", true));
        Assert.False(ThemeFilenameMatcher.Matches("theme.mp3", "OshiNoKoS3-OP1-NCBD1080", "op1", ".mp3", false));
    }

    [Fact]
    public void RejectsWrongMediaExtensionAndUnrelatedNames()
    {
        Assert.False(ThemeFilenameMatcher.Matches("4770__OshiNoKoS3-OP1-NCBD1080__50.webm", "OshiNoKoS3-OP1-NCBD1080", "op1", ".mp3", false));
        Assert.False(ThemeFilenameMatcher.Matches("4770__OshiNoKoS3-OP2-NCBD1080__50.mp3", "OshiNoKoS3-OP1-NCBD1080", "op1", ".mp3", false));
    }
}
