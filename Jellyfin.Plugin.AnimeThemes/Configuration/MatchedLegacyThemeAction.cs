namespace Jellyfin.Plugin.AnimeThemes.Configuration;

/// <summary>
/// Determines how a legacy root theme with one known destination is migrated.
/// </summary>
public enum MatchedLegacyThemeAction
{
    /// <summary>
    /// Moves the existing file to its resolved target without downloading it again.
    /// </summary>
    Move,

    /// <summary>
    /// Removes the existing file so the resolved target is downloaded again.
    /// </summary>
    DeleteAndRedownload,
}
