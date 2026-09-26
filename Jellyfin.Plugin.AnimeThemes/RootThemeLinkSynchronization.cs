using System.Collections.Generic;

namespace Jellyfin.Plugin.AnimeThemes;

internal sealed record RootThemeLinkSynchronization(bool Changed, IReadOnlyCollection<string> Conflicts);
