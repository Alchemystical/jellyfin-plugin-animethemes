namespace Jellyfin.Plugin.AnimeThemes.Configuration;

/// <summary>
/// Determines how legacy root themes without one known destination are handled.
/// </summary>
public enum UnmatchedLegacyThemeAction
{
    /// <summary>
    /// Removes the file.
    /// </summary>
    Delete,

    /// <summary>
    /// Leaves the file at the Series root.
    /// </summary>
    Keep,
}
