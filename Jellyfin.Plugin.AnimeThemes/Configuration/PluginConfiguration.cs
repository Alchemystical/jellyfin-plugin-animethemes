using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AnimeThemes.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // set default options here
        DegreeOfParallelism = 1;
        ForceSync = false;
        IncludeLibraries = "^.*$";
        ExcludeLibraries = string.Empty;

        AudioSettings = new MediaTypeConfiguration()
        {
            FetchType = FetchType.Single,
            IgnoreOverlapping = true,
            IgnoreThemesWithCredits = false,
            IgnoreOPs = false,
            IgnoreEDs = false,
            Volume = 0.5,
        };

        VideoSettings = new MediaTypeConfiguration()
        {
            FetchType = FetchType.None,
            IgnoreOverlapping = true,
            IgnoreThemesWithCredits = true,
            IgnoreOPs = false,
            IgnoreEDs = false,
            Volume = 0.0,
        };
        AudioShokoGroupPlacement = ShokoGroupPlacement.LegacyRootAniDb;
        VideoShokoGroupPlacement = ShokoGroupPlacement.LegacyRootAniDb;
        MigrateLegacyShokoGroupThemes = false;
        MatchedLegacyShokoThemeAction = MatchedLegacyThemeAction.Move;
        UnmatchedLegacyShokoThemeAction = UnmatchedLegacyThemeAction.Delete;

        MovieSettings = new CollectionTypeConfiguration();
    }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public int DegreeOfParallelism { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sync should enforce conformity.
    /// </summary>
    public bool ForceSync { get; set; }

    /// <summary>
    /// Gets or sets a regex for libraries to consider.
    /// </summary>
    public string IncludeLibraries { get; set; }

    /// <summary>
    /// Gets or sets a regex for libraries to exclude from consideration.
    /// </summary>
    public string ExcludeLibraries { get; set; }

    /// <summary>
    /// Gets or sets the audio settings.
    /// </summary>
    public MediaTypeConfiguration AudioSettings { get; set; }

    /// <summary>
    /// Gets or sets the video settings.
    /// </summary>
    public MediaTypeConfiguration VideoSettings { get; set; }

    /// <summary>
    /// Gets or sets where Shoko Group audio themes are written.
    /// </summary>
    public ShokoGroupPlacement AudioShokoGroupPlacement { get; set; }

    /// <summary>
    /// Gets or sets where Shoko Group video themes are written.
    /// </summary>
    public ShokoGroupPlacement VideoShokoGroupPlacement { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether unambiguous plugin-generated Shoko Group themes are moved from the Series root to their configured non-legacy target.
    /// </summary>
    public bool MigrateLegacyShokoGroupThemes { get; set; }

    /// <summary>
    /// Gets or sets how a legacy root theme with one resolved target is migrated.
    /// </summary>
    public MatchedLegacyThemeAction MatchedLegacyShokoThemeAction { get; set; }

    /// <summary>
    /// Gets or sets how an unmatched or ambiguous legacy root theme is handled during migration.
    /// </summary>
    public UnmatchedLegacyThemeAction UnmatchedLegacyShokoThemeAction { get; set; }

    /// <summary>
    /// Gets or sets the download settings for the movie type.
    /// </summary>
    public CollectionTypeConfiguration MovieSettings { get; set; }
}
