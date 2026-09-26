using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AnimeThemes.Configuration;
using Jellyfin.Plugin.AnimeThemes.Exceptions;
using Jellyfin.Plugin.AnimeThemes.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using MediaType = Jellyfin.Plugin.AnimeThemes.Models.MediaType;
using Video = Jellyfin.Plugin.AnimeThemes.Models.Video;

namespace Jellyfin.Plugin.AnimeThemes;

/// <summary>
/// Class that is responsible for downloading themes.
/// </summary>
public class AnimeThemesDownloader : IDisposable
{
    private const string ThemeMusicFileName = "theme.mp3";
    private const string ThemeMusicDirectory = "theme-music";
    private const string ThemeVideoDirectory = "backdrops";

    private readonly HttpClient _client;
    private readonly AnimeThemesApi _api;
    private readonly ILogger<AnimeThemesDownloader> _logger;
    private readonly IMediaEncoder _mediaEncoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimeThemesDownloader"/> class.
    /// </summary>
    /// <param name="mediaEncoder">Media Encoder to convert OGG files.</param>
    /// <param name="clientFactory">Client factory.</param>
    /// <param name="logger">Logger.</param>
    public AnimeThemesDownloader(IMediaEncoder mediaEncoder, IHttpClientFactory clientFactory, ILogger<AnimeThemesDownloader> logger)
    {
        _mediaEncoder = mediaEncoder;
        _logger = logger;
        _client = new HttpClient();
        _api = new AnimeThemesApi(clientFactory, logger);
    }

    /// <summary>
    /// Checks if this item should be processed.
    /// </summary>
    /// <param name="item">Item to check.</param>
    /// <param name="configuration">Plugin configuration.</param>
    /// <returns>Whether item should be processed.</returns>
    public bool ShouldUpdate(BaseItem item, PluginConfiguration configuration)
    {
        return (item.GetBaseItemKind() == BaseItemKind.Series || item.GetBaseItemKind() == BaseItemKind.Movie)
            && TryGetAniDbId(item, configuration, out _)
            && !IsSatisfied(item, configuration);
    }

    /// <summary>
    /// Resolves a list of BaseItems to their download targets and corresponding anime objects.
    /// </summary>
    /// <param name="items">Chunk of items.</param>
    /// <param name="seasonsBySeries">Physical Seasons belonging to Shoko Group Series.</param>
    /// <param name="configuration">Plugin configuration to do some pre-filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A filtered list of download targets with their corresponding anime.</returns>
    public async IAsyncEnumerable<ItemWithAnime> ResolveItems(
        BaseItem[] items,
        ILookup<Guid, Season> seasonsBySeries,
        PluginConfiguration configuration,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var ids = new HashSet<int>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsShokoGroup(item) || !RequiresShokoGroupProcessing(item, configuration))
            {
                AddAniDbId(item, configuration, ids);
                continue;
            }

            if (UsesLegacyRootAniDb(configuration))
            {
                AddAniDbId(item, configuration, ids);
            }

            foreach (var season in seasonsBySeries[item.Id])
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddAniDbId(season, configuration, ids);
            }
        }

        var animeByAniDb = await FindAnimeByAniDbId(ids, cancellationToken).ConfigureAwait(false);

        var emittedLegacyIds = new HashSet<int>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsShokoGroup(item) && RequiresShokoGroupProcessing(item, configuration))
            {
                await foreach (var target in ResolveShokoGroup(item, seasonsBySeries[item.Id], animeByAniDb, configuration, cancellationToken).ConfigureAwait(false))
                {
                    yield return target;
                }

                continue;
            }

            if (!TryGetAniDbId(item, configuration, out var id)
                || !emittedLegacyIds.Add(id)
                || !animeByAniDb.TryGetValue(id, out var anime)
                || anime.Length == 0)
            {
                continue;
            }

            yield return new ItemWithAnime(item, new ReadOnlyCollection<Anime>(anime));
        }
    }

    /// <summary>
    /// Determines whether a Shoko Group must be resolved from its physical Seasons.
    /// </summary>
    /// <param name="item">Candidate library item.</param>
    /// <param name="configuration">Plugin configuration.</param>
    /// <returns>Whether the item requires Shoko Group member resolution.</returns>
    public bool RequiresShokoGroupProcessing(BaseItem item, PluginConfiguration configuration)
    {
        return IsShokoGroup(item)
            && ((configuration.AudioShokoGroupPlacement != ShokoGroupPlacement.LegacyRootAniDb
                    && configuration.AudioSettings.FetchType != FetchType.None)
                || (configuration.VideoShokoGroupPlacement != ShokoGroupPlacement.LegacyRootAniDb
                    && configuration.VideoSettings.FetchType != FetchType.None)
                || (configuration.MigrateLegacyShokoGroupThemes
                    && (configuration.AudioSettings.FetchType != FetchType.None
                        || configuration.VideoSettings.FetchType != FetchType.None)));
    }

    /// <summary>
    /// Processes an item, downloading its theme if applicable.
    /// </summary>
    /// <param name="itemWithAnime">The DB item, target media type, and anime to process.</param>
    /// <param name="configuration">Configuration of the plugin.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that runs until the item is done processing.</returns>
    public async ValueTask HandleAsync(ItemWithAnime itemWithAnime, PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        var item = itemWithAnime.Item;
        _logger.LogInformation(
            "[{Id}] Attempting to filter theme songs for: {Name} (AniDB={AniId})",
            item.Id,
            item.Name,
            itemWithAnime.Anime.First().Id);

        bool isMovie = item.GetBaseItemKind() == BaseItemKind.Movie;
        var collectionTypeConfig = isMovie ? configuration.MovieSettings : new CollectionTypeConfiguration { AudioSettings = configuration.AudioSettings, VideoSettings = configuration.VideoSettings };

        bool videoChanged = false;
        bool audioChanged = false;
        if (itemWithAnime.MediaType is null || itemWithAnime.MediaType == MediaType.Video)
        {
            videoChanged = await ProcessMediaType(
                MediaType.Video,
                itemWithAnime.Anime,
                item,
                configuration.ForceSync,
                collectionTypeConfig,
                itemWithAnime.UseSourceUniqueFileNames,
                cancellationToken).ConfigureAwait(false);
        }

        if (itemWithAnime.MediaType is null || itemWithAnime.MediaType == MediaType.Audio)
        {
            audioChanged = await ProcessMediaType(
                MediaType.Audio,
                itemWithAnime.Anime,
                item,
                configuration.ForceSync,
                collectionTypeConfig,
                itemWithAnime.UseSourceUniqueFileNames,
                cancellationToken).ConfigureAwait(false);
        }

        if (videoChanged || audioChanged)
        {
            _logger.LogInformation("[{Id}] Saving metadata", item.Id);
            await item.RefreshMetadata(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation("[{Id}] Finished without changes", item.Id);
        }
    }

    private async ValueTask<Dictionary<int, Anime[]>> FindAnimeByAniDbId(IEnumerable<int> ids, CancellationToken cancellationToken)
    {
        var animeByAniDb = new Dictionary<int, Anime[]>();
        foreach (var idsChunk in ids.Chunk(100))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _api.FindByAniDbId(idsChunk, cancellationToken).ConfigureAwait(false);
            foreach (var (id, anime) in result)
            {
                animeByAniDb.Add(id, anime);
            }
        }

        return animeByAniDb;
    }

    private async IAsyncEnumerable<ItemWithAnime> ResolveShokoGroup(
        BaseItem group,
        IEnumerable<Season> seasons,
        IReadOnlyDictionary<int, Anime[]> animeByAniDb,
        PluginConfiguration configuration,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sources = seasons
            .Where(season => !season.IsVirtualItem)
            .Select(season => TryGetAniDbId(season, configuration, out var id) ? new GroupSource(season, id) : null)
            .Where(source => source is not null)
            .Select(source => source!)
            .GroupBy(source => source.Season.Id)
            .Select(source => source.First())
            .OrderBy(source => source.Season.IndexNumber)
            .ThenBy(source => source.Season.Id)
            .ToArray();

        if (sources.Length == 0)
        {
            LogUnrecoverableShokoGroupSources(group, configuration);
        }

        var targets = ResolveShokoGroupMediaType(MediaType.Audio, group, sources, animeByAniDb, configuration, cancellationToken)
            .Concat(ResolveShokoGroupMediaType(MediaType.Video, group, sources, animeByAniDb, configuration, cancellationToken))
            .ToArray();

        if (configuration.MigrateLegacyShokoGroupThemes)
        {
            await MigrateLegacyThemes(group, sources, targets, configuration, cancellationToken).ConfigureAwait(false);
        }

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return target;
        }
    }

    private IEnumerable<ItemWithAnime> ResolveShokoGroupMediaType(
        MediaType mediaType,
        BaseItem group,
        IReadOnlyCollection<GroupSource> sources,
        IReadOnlyDictionary<int, Anime[]> animeByAniDb,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var placement = mediaType == MediaType.Audio
            ? configuration.AudioShokoGroupPlacement
            : configuration.VideoShokoGroupPlacement;
        cancellationToken.ThrowIfCancellationRequested();
        if (placement == ShokoGroupPlacement.LegacyRootAniDb)
        {
            if (TryGetAniDbId(group, configuration, out var rootId)
                && animeByAniDb.TryGetValue(rootId, out var rootAnime)
                && rootAnime.Length > 0)
            {
                yield return new ItemWithAnime(group, new ReadOnlyCollection<Anime>(rootAnime), mediaType);
            }

            yield break;
        }

        if (placement == ShokoGroupPlacement.PerSeason)
        {
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (animeByAniDb.TryGetValue(source.AniDbId, out var seasonAnime) && seasonAnime.Length > 0)
                {
                    yield return new ItemWithAnime(source.Season, new ReadOnlyCollection<Anime>(seasonAnime), mediaType, true);
                }
            }

            yield break;
        }

        if (placement == ShokoGroupPlacement.SeriesMix)
        {
            var mixedAnime = sources
                .SelectMany(source => animeByAniDb.GetValueOrDefault(source.AniDbId) ?? [])
                .DistinctBy(anime => anime.Id)
                .ToArray();
            if (mixedAnime.Length > 0)
            {
                yield return new ItemWithAnime(group, new ReadOnlyCollection<Anime>(mixedAnime), mediaType, true);
            }

            yield break;
        }

        throw new ArgumentOutOfRangeException(nameof(configuration), placement, "Unknown Shoko Group placement.");
    }

    private async ValueTask MigrateLegacyThemes(
        BaseItem group,
        IReadOnlyCollection<GroupSource> sources,
        IReadOnlyCollection<ItemWithAnime> targets,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var sourceItems = new[] { group }
            .Concat(sources.Select(source => (BaseItem)source.Season))
            .GroupBy(item => item.Id)
            .Select(items => items.First())
            .ToArray();
        var migrationTargets = targets.Where(target => target.MediaType is not null).ToArray();
        if (migrationTargets.Length == 0)
        {
            _logger.LogInformation("[{Id}] Skipping Shoko Group theme placement migration because no resolved target is available", group.Id);
            return;
        }

        var targetLinks = migrationTargets
            .SelectMany(target => GetThemeLinks(
                    target.MediaType!.Value,
                    target.Anime,
                    target.MediaType == MediaType.Audio ? configuration.AudioSettings : configuration.VideoSettings,
                    target.UseSourceUniqueFileNames)
                .Select(link => new PlacementMigrationTarget(target, link)))
            .ToArray();
        var desiredPaths = targetLinks
            .Select(target => Path.Combine(target.Target.Item.ContainingFolderPath, target.Link.Filepath))
            .ToHashSet(StringComparer.Ordinal);
        var targetCountsByMediaType = targetLinks
            .GroupBy(target => target.Target.MediaType!.Value)
            .ToDictionary(targetsByMediaType => targetsByMediaType.Key, targetsByMediaType => targetsByMediaType.Count());
        var candidates = targetLinks
            .SelectMany(target => sourceItems.SelectMany(source => GetMigrationSourcePaths(source, target.Target.MediaType!.Value)
                .Where(path => ThemeFilenameMatcher.Matches(
                    path,
                    target.Link.SourceFilename,
                    target.Link.ThemeSlug,
                    target.Link.Extension,
                    targetCountsByMediaType[target.Target.MediaType!.Value] == 1))
                .Select(path => new PlacementMigrationCandidate(source, target.Target, target.Link, path))))
            .Where(candidate => !string.Equals(candidate.SourcePath, Path.Combine(candidate.Target.Item.ContainingFolderPath, candidate.Link.Filepath), StringComparison.Ordinal))
            .DistinctBy(candidate => (candidate.SourcePath, candidate.Target.Item.Id, candidate.Link.Filepath))
            .ToArray();

        var retainedSourcePaths = new HashSet<string>(StringComparer.Ordinal);
        var affectedItems = new HashSet<BaseItem>();
        foreach (var candidatesBySource in candidates.GroupBy(candidate => candidate.SourcePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidatesBySource.Count() != 1)
            {
                _logger.LogWarning("[{Id}] Shoko Group theme {Path} matches multiple placement targets and is treated as unmatched", group.Id, candidatesBySource.Key);
                continue;
            }

            var candidate = candidatesBySource.First();
            if (!File.Exists(candidate.SourcePath))
            {
                continue;
            }

            if (configuration.MatchedLegacyShokoThemeAction == MatchedLegacyThemeAction.DeleteAndRedownload)
            {
                File.Delete(candidate.SourcePath);
                affectedItems.Add(candidate.Source);
                _logger.LogInformation("[{Id}] Removed matched Shoko Group theme {Path}; it will be downloaded again", group.Id, candidate.SourcePath);
                continue;
            }

            var destinationPath = Path.Combine(candidate.Target.Item.ContainingFolderPath, candidate.Link.Filepath);
            if (TryMoveLegacyTheme(candidate.SourcePath, destinationPath, group.Id))
            {
                affectedItems.Add(candidate.Source);
                affectedItems.Add(candidate.Target.Item);
            }
            else
            {
                retainedSourcePaths.Add(candidate.SourcePath);
            }
        }

        foreach (var source in sourceItems)
        {
            foreach (var mediaType in migrationTargets.Select(target => target.MediaType!.Value).Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directoryPath = Path.Combine(source.ContainingFolderPath, GetThemeDirectory(mediaType));
                var pattern = mediaType == MediaType.Audio ? "*.mp3" : "*.webm";
                if (!Directory.Exists(directoryPath))
                {
                    continue;
                }

                foreach (var path in Directory.GetFiles(directoryPath, pattern))
                {
                    if (desiredPaths.Contains(path) || retainedSourcePaths.Contains(path))
                    {
                        continue;
                    }

                    if (configuration.UnmatchedLegacyShokoThemeAction == UnmatchedLegacyThemeAction.Delete)
                    {
                        File.Delete(path);
                        affectedItems.Add(source);
                        _logger.LogInformation("[{Id}] Removed unmatched Shoko Group theme {Path}", group.Id, path);
                    }
                    else
                    {
                        _logger.LogInformation("[{Id}] Kept unmatched Shoko Group theme {Path}", group.Id, path);
                    }
                }
            }
        }

        foreach (var item in affectedItems)
        {
            await item.RefreshMetadata(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string GetThemeDirectory(MediaType mediaType)
    {
        return mediaType == MediaType.Audio ? ThemeMusicDirectory : ThemeVideoDirectory;
    }

    private static IEnumerable<string> GetMigrationSourcePaths(BaseItem source, MediaType mediaType)
    {
        var directoryPath = Path.Combine(source.ContainingFolderPath, GetThemeDirectory(mediaType));
        var pattern = mediaType == MediaType.Audio ? "*.mp3" : "*.webm";
        if (Directory.Exists(directoryPath))
        {
            foreach (var path in Directory.GetFiles(directoryPath, pattern))
            {
                yield return path;
            }
        }

        if (mediaType == MediaType.Audio)
        {
            yield return Path.Combine(source.ContainingFolderPath, ThemeMusicFileName);
        }
    }

    private bool TryMoveLegacyTheme(string sourcePath, string destinationPath, Guid groupId)
    {
        if (File.Exists(destinationPath))
        {
            _logger.LogWarning("[{Id}] Keeping Shoko Group theme {Path} because destination {Destination} already exists", groupId, sourcePath, destinationPath);
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        try
        {
            File.Move(sourcePath, destinationPath);
            _logger.LogInformation("[{Id}] Migrated Shoko Group theme {Source} to {Destination}", groupId, sourcePath, destinationPath);
            return true;
        }
        catch (IOException)
        {
            try
            {
                File.Copy(sourcePath, destinationPath, false);
                File.Delete(sourcePath);
                _logger.LogInformation("[{Id}] Copied and removed Shoko Group theme {Source} to {Destination}", groupId, sourcePath, destinationPath);
                return true;
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "[{Id}] Could not migrate Shoko Group theme {Source} to {Destination}", groupId, sourcePath, destinationPath);
                return false;
            }
        }
    }

    private void LogUnrecoverableShokoGroupSources(BaseItem group, PluginConfiguration configuration)
    {
        if (TryGetAniDbId(group, configuration, out var rootId))
        {
            _logger.LogWarning(
                "[{Id}] Shoko Group {Name} exposes root AniDB ID {AniDbId}, but no physical Season AniDB IDs; cannot recover member sources for the configured placement.",
                group.Id,
                group.Name,
                rootId);
            return;
        }

        _logger.LogWarning(
            "[{Id}] Shoko Group {Name} has no physical Season AniDB IDs; cannot recover member sources for the configured placement.",
            group.Id,
            group.Name);
    }

    private static bool UsesLegacyRootAniDb(PluginConfiguration configuration)
    {
        return configuration.AudioShokoGroupPlacement == ShokoGroupPlacement.LegacyRootAniDb
            || configuration.VideoShokoGroupPlacement == ShokoGroupPlacement.LegacyRootAniDb;
    }

    private static bool IsShokoGroup(BaseItem item)
    {
        return item.GetBaseItemKind() == BaseItemKind.Series
            && item.TryGetProviderId("Shoko Group", out _);
    }

    private void AddAniDbId(BaseItem item, PluginConfiguration configuration, ISet<int> ids)
    {
        if (TryGetAniDbId(item, configuration, out var id))
        {
            ids.Add(id);
        }
    }

    private async ValueTask<bool> ProcessMediaType(
        MediaType type,
        IEnumerable<Anime> anime,
        BaseItem item,
        bool forceSync,
        CollectionTypeConfiguration configuration,
        bool useSourceUniqueFileNames,
        CancellationToken cancellationToken = default)
    {
        var settings = type == MediaType.Audio ? configuration.AudioSettings : configuration.VideoSettings;
        var links = GetThemeLinks(type, anime, settings, useSourceUniqueFileNames);

        // Before we start the download, make sure the folders are in a clean state.
        if (forceSync)
        {
            if (type == MediaType.Audio)
            {
                // We don't need this file because we're using the directory variant.
                RemoveFile(item, ThemeMusicFileName);
            }

            CleanDirectory(item, type, links.Select(it => Path.GetFileName(it.Filepath)));
        }

        bool changesMade = false;
        foreach (var link in links)
        {
            // Download if needed.
            changesMade |= await Download(type, link.Url, item, link.Filepath, settings.Volume, cancellationToken).ConfigureAwait(false);
        }

        return changesMade;
    }

    private ThemeLink[] GetThemeLinks(
        MediaType type,
        IEnumerable<Anime> anime,
        MediaTypeConfiguration settings,
        bool useSourceUniqueFileNames)
    {
        var distinctThemes = anime
            .SelectMany(source => GetBestThemes(source, settings).Select(theme => new SourcedTheme(source, theme)))
            .DistinctBy(theme => (theme.Anime.Id, theme.Theme.Theme.Id));

        // Pick themes only after every group member has been aggregated.
        var requiredThemes = PickThemes(settings.FetchType, distinctThemes);
        return ExtractLinks(type, requiredThemes, settings, useSourceUniqueFileNames).ToArray();
    }

    private IEnumerable<T> PickThemes<T>(FetchType fetchType, IEnumerable<T> themes)
    {
        switch (fetchType)
        {
            case FetchType.None:
                return [];
            case FetchType.Single:
                return themes.Take(1);
            case FetchType.All:
                return themes;
            default:
                throw new ArgumentOutOfRangeException($"Unknown fetch type: {fetchType}");
        }
    }

    private IEnumerable<ThemeLink> ExtractLinks(
        MediaType type,
        IEnumerable<SourcedTheme> themes,
        MediaTypeConfiguration settings,
        bool useSourceUniqueFileNames)
    {
        bool isAudio = type == MediaType.Audio;
        var extension = isAudio ? ".mp3" : ".webm";
        var directory = isAudio ? ThemeMusicDirectory : ThemeVideoDirectory;

        foreach (var source in themes)
        {
            var url = isAudio ? source.Theme.Audio.Link : source.Theme.Video.Link;
            var sourceFilename = isAudio ? source.Theme.Audio.Filename : source.Theme.Video.Filename;
            var filename = useSourceUniqueFileNames ? $"{source.Anime.Id}__{sourceFilename}" : sourceFilename;
            var legacyFilename = $"{sourceFilename}__{settings.Volume * 100:0}{extension}";
            var filepath = Path.Combine(directory, $"{filename}__{settings.Volume * 100:0}{extension}");

            yield return new ThemeLink(url, filepath, sourceFilename, source.Theme.Theme.Slug, extension);
        }
    }

    private void RemoveFile(BaseItem series, string filename)
    {
        var path = Path.Combine(series.ContainingFolderPath, filename);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void CleanDirectory(BaseItem series, MediaType mediaType, IEnumerable<string> allowedNames)
    {
        var directory = mediaType == MediaType.Audio ? ThemeMusicDirectory : ThemeVideoDirectory;
        var searchPattern = mediaType == MediaType.Audio ? "*.mp3" : "*.webm";

        var path = Path.Combine(series.ContainingFolderPath, directory);
        if (!Directory.Exists(path))
        {
            return;
        }

        var allowedNamesSet = allowedNames.ToHashSet();

        foreach (var filepath in Directory.GetFiles(path, searchPattern))
        {
            var name = Path.GetFileName(filepath);
            if (!allowedNamesSet.Contains(name))
            {
                _logger.LogInformation("[{Id}] Removing obsolete theme: {Theme}", series.Id, filepath);
                File.Delete(filepath);
            }
        }
    }

    private async ValueTask<bool> Download(MediaType type, string url, BaseItem item, string relativePath, double volume = 1.0, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(item.ContainingFolderPath, relativePath);
        if (File.Exists(path))
        {
            // Nothing to do
            return false;
        }

        var tempFile = Path.GetTempFileName();
        try
        {
            // Make sure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            {
                _logger.LogInformation("[{Id}] Downloading {Url} to {Path}", item.Id, url, path);
                using var downloadStream = await _client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
                using var fileStream = File.OpenWrite(tempFile);

                // Download file
                await downloadStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            // Convert OGG
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,

                    // Must consume both or ffmpeg may hang due to deadlocks.
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    FileName = _mediaEncoder.EncoderPath,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ErrorDialog = false,
                    ArgumentList = { "-i", tempFile }
                },
                EnableRaisingEvents = true
            };

            // Update arguments
            var arguments = process.StartInfo.ArgumentList;
            if (type == MediaType.Video)
            {
                // Copy video stream
                arguments.Add("-c:v");
                arguments.Add("copy");
            }

            if (volume < 0.01 && type == MediaType.Video)
            {
                // Mute videos when volume is low enough
                arguments.Add("-an");
            }
            else
            {
                arguments.Add("-filter:a");
                arguments.Add(string.Create(CultureInfo.InvariantCulture, $"volume={volume:0.00}"));
            }

            arguments.Add(path);

            process.Start();

            var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var commandInfo = $"Command line: {process.StartInfo.FileName} {string.Join(" ", arguments)}";
                throw new ConversionException(process.ExitCode, commandInfo + "\n" + error);
            }

            _logger.LogInformation("[{Id}] Successfully downloaded theme song!", item.Id);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Download failed");
            return false;
        }
        finally
        {
            File.Delete(tempFile);
        }

        return true;
    }

    private bool IsSatisfied(BaseItem item, PluginConfiguration configuration)
    {
        var audioSatisfied = item.GetThemeSongs().Any() || configuration.AudioSettings.FetchType == FetchType.None;
        var videoSatisfied = item.GetThemeVideos().Any() || configuration.VideoSettings.FetchType == FetchType.None;

        return audioSatisfied && videoSatisfied;
    }

    private bool TryGetAniDbId(BaseItem item, PluginConfiguration configuration, out int id)
    {
        id = -1;

        // Themes are stored only for Series, Movies, and physical Shoko Group Seasons.
        if (item.GetBaseItemKind() != BaseItemKind.Series
            && item.GetBaseItemKind() != BaseItemKind.Movie
            && item.GetBaseItemKind() != BaseItemKind.Season)
        {
            return false;
        }

        if (item.TryGetProviderId("AniDB", out var idAsString)
            && int.TryParse(idAsString, NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets themes roughly sorted by relevance and filtered as needed.
    /// </summary>
    /// <param name="anime">The anime in question.</param>
    /// <param name="settings">The configuration that sets the rules.</param>
    /// <returns>A filtered and sorted and flattened enumerable of themes.</returns>
    private IEnumerable<FlattenedTheme> GetBestThemes(Anime anime, MediaTypeConfiguration settings)
    {
        return anime.Themes.SelectMany(theme => theme.Entries.SelectMany(entry => entry.Videos.Select(video => Wrap(theme, entry, video))))
            .OrderBy(Rate)
            .Where(it => !settings.IgnoreOverlapping || it.Video.Overlap == OverlapType.None)
            .Where(it => !settings.IgnoreThemesWithCredits || it.Video.Creditless)
            .Where(it => !settings.IgnoreEDs || it.Theme.Type != ThemeType.ED)
            .Where(it => !settings.IgnoreOPs || it.Theme.Type != ThemeType.OP);
    }

    private FlattenedTheme Wrap(AnimeTheme theme, AnimeThemeEntry entry, Video video)
    {
        return new FlattenedTheme(theme, entry, video, video.Audio);
    }

    private double Rate(FlattenedTheme theme)
    {
        double score = 0;

        if (theme.Entry.Nsfw)
        {
            score += 10;
        }

        if (theme.Entry.Spoiler)
        {
            // Huge penalty for spoilers
            score += 50;
        }

        // Big penalties for overlap
        switch (theme.Video.Overlap)
        {
            case OverlapType.Over:
                score += 20;
                break;
            case OverlapType.Transition:
                score += 15;
                break;
        }

        // Small penalties for source
        switch (theme.Video.Source)
        {
            case VideoSource.LD:
            case VideoSource.VHS:
                score += 10;
                break;
            case VideoSource.WEB:
            case VideoSource.RAW:
                score += 5;
                break;
        }

        // Medium penalty for credits
        if (!theme.Video.Creditless)
        {
            score += 10;
        }

        return score;
    }

    /// <summary>
    /// Dispose.
    /// </summary>
    /// <param name="disposing">Whether we're disposing.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _api.Dispose();
            _client.Dispose();
        }
    }

    /// <summary>
    /// Dispose.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private sealed record GroupSource(Season Season, int AniDbId);

    private sealed record SourcedTheme(Anime Anime, FlattenedTheme Theme);

    private sealed record ThemeLink(string Url, string Filepath, string SourceFilename, string? ThemeSlug, string Extension);

    private sealed record PlacementMigrationTarget(ItemWithAnime Target, ThemeLink Link);

    private sealed record PlacementMigrationCandidate(BaseItem Source, ItemWithAnime Target, ThemeLink Link, string SourcePath);
}
