using System.Collections.Generic;
using System.Net.Http;
using Jellyfin.Plugin.AnimeThemes.Configuration;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AnimeThemes.Tests;

/// <summary>
/// Behavioural tests for opt-in Shoko Group theme placement.
/// </summary>
public sealed class ShokoGroupPlacementTests
{
    [Fact]
    public void ConfigurationDefaultsKeepLegacyPlacement()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal(ShokoGroupPlacement.LegacyRootAniDb, configuration.AudioShokoGroupPlacement);
        Assert.Equal(ShokoGroupPlacement.LegacyRootAniDb, configuration.VideoShokoGroupPlacement);
    }

    [Fact]
    public void MigrationDefaultsToDisabledWithExplicitFileActions()
    {
        var configuration = new PluginConfiguration();

        Assert.False(configuration.MigrateLegacyShokoGroupThemes);
        Assert.Equal(MatchedLegacyThemeAction.Move, configuration.MatchedLegacyShokoThemeAction);
        Assert.Equal(UnmatchedLegacyThemeAction.Delete, configuration.UnmatchedLegacyShokoThemeAction);
        Assert.False(configuration.CheckShokoRootThemeLinks);
        Assert.False(configuration.LinkPerSeasonThemesFromShokoGroupRoot);
    }

    [Fact]
    public void MigrationEnablesShokoResolutionForLegacyPlacement()
    {
        using var downloader = CreateDownloader();
        var configuration = new PluginConfiguration
        {
            MigrateLegacyShokoGroupThemes = true,
        };

        Assert.True(downloader.RequiresShokoGroupProcessing(CreateShokoGroup(), configuration));
    }

    [Fact]
    public void LegacyPlacementDoesNotEnableShokoMemberProcessing()
    {
        using var downloader = CreateDownloader();

        Assert.False(downloader.RequiresShokoGroupProcessing(CreateShokoGroup(), new PluginConfiguration()));
    }

    [Fact]
    public void NonLegacyAudioPlacementEnablesOnlyShokoGroups()
    {
        using var downloader = CreateDownloader();
        var configuration = new PluginConfiguration
        {
            AudioShokoGroupPlacement = ShokoGroupPlacement.SeriesMix,
        };

        Assert.True(downloader.RequiresShokoGroupProcessing(CreateShokoGroup(), configuration));
        Assert.False(downloader.RequiresShokoGroupProcessing(new Series(), configuration));
    }

    [Fact]
    public void NonLegacyPlacementWithDisabledMediaDoesNotEnableMemberProcessing()
    {
        using var downloader = CreateDownloader();
        var configuration = new PluginConfiguration
        {
            AudioShokoGroupPlacement = ShokoGroupPlacement.PerSeason,
        };
        configuration.AudioSettings.FetchType = FetchType.None;

        Assert.False(downloader.RequiresShokoGroupProcessing(CreateShokoGroup(), configuration));
    }

    [Fact]
    public void RootLinkCheckOnlyEnablesMemberProcessingWhenRootLinksAreEnabled()
    {
        using var downloader = CreateDownloader();
        var configuration = new PluginConfiguration
        {
            CheckShokoRootThemeLinks = true,
        };

        Assert.False(downloader.RequiresShokoGroupProcessing(CreateShokoGroup(), configuration));

        configuration.LinkPerSeasonThemesFromShokoGroupRoot = true;

        Assert.True(downloader.RequiresShokoGroupProcessing(CreateShokoGroup(), configuration));
    }

    private static AnimeThemesDownloader CreateDownloader()
    {
        return new AnimeThemesDownloader(null!, new TestHttpClientFactory(), NullLogger<AnimeThemesDownloader>.Instance);
    }

    private static Series CreateShokoGroup()
    {
        return new Series
        {
            ProviderIds = new Dictionary<string, string>
            {
                ["Shoko Group"] = "2579",
            },
        };
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}
