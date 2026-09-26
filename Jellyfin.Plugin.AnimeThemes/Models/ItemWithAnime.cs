using System.Collections.ObjectModel;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.AnimeThemes.Models;

/// <summary>
/// BaseItem and the corresponding anime object.
/// </summary>
/// <param name="Item">BaseItem that receives the themes.</param>
/// <param name="Anime">Anime objects whose themes are downloaded.</param>
/// <param name="MediaType">Optional media type to process; when omitted, both types are processed.</param>
/// <param name="UseSourceUniqueFileNames">Whether output names must distinguish AnimeThemes anime sources.</param>
public sealed record ItemWithAnime(
    BaseItem Item,
    ReadOnlyCollection<Anime> Anime,
    MediaType? MediaType = null,
    bool UseSourceUniqueFileNames = false);
