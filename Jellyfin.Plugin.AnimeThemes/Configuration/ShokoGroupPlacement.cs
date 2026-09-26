namespace Jellyfin.Plugin.AnimeThemes.Configuration;

/// <summary>
/// Determines where themes from the AniDB members of a Shoko Group are written.
/// </summary>
public enum ShokoGroupPlacement
{
    /// <summary>
    /// Uses the Series root AniDB ID, preserving the legacy lookup and output behavior.
    /// </summary>
    LegacyRootAniDb,

    /// <summary>
    /// Writes each physical Season's AniDB themes to that Season's folder.
    /// </summary>
    PerSeason,

    /// <summary>
    /// Mixes all physical Season AniDB themes into the Series root folder.
    /// </summary>
    SeriesMix
}
