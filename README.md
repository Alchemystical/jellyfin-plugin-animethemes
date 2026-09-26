# AnimeThemes for Jellyfin

Plugin for [Jellyfin](https://jellyfin.org/) that fetches anime themes (OPs and EDs)
from [animethemes.moe](https://animethemes.moe). Both audio files and video files are supported.

**An AniDB ID is used to find matching themes, so you'll need to install the AniDB plugin. All other series are ignored.**

## How to use

The plugin creates a new scheduled task that you can configure. By default, it will run every 6 hours.
Also make sure to enable theme music / videos in your display settings.

## How to install

### Plugin Manager

1. Add the following repository to your plugin repositories:
   https://raw.githubusercontent.com/EusthEnoptEron/jellyfin-plugin-animethemes/main/manifest.json
2. Find the plugin in the catalog and install it.

### Manually

1. [Grab the latest build.](https://github.com/EusthEnoptEron/jellyfin-plugin-animethemes/actions/workflows/build.yaml)
2. Download it into your plugins folder [as described here](https://jellyfin.org/docs/general/server/plugins/).

## Configuration

| Configuration                           | Description                                                                                               |
|-----------------------------------------|-----------------------------------------------------------------------------------------------------------|
| Degree of parallelism                   | How many items should be downloaded in parallel. Default is set to 1 to go easy on the servers.           |
| Ignore themes that overlap with episode | Some shows transition into their themes. Use this setting to filter those themes out.                     |
| Ignore themes with credits              | Whether to skip themes with credits in them. Useful for video downloads.                                  |
| Ignore OP themes                        | Use this to control whether OPs are considered.                                                           |
| Ignore ED themes                        | Use this to control whether EDs are considered.                                                           |
| Fetch type                              | Determines if you want all distinct themes, only the best match, or none at all.                          |
| Shoko Group placement (audio/video)   | For Shoko Groups only. Legacy root AniDB keeps existing behavior; Per Season writes to each physical Season; Series Mix writes all recovered Season themes to the Series root. A group with no physical Season AniDB IDs (for example, an unrecoverable Season Merging layout) is skipped and logged. |
| Legacy Shoko Group migration            | Disabled by default. When enabled with non-legacy placement, each scan can move a uniquely matched root theme or delete it and download again. Unmatched or ambiguous root files default to delete; keeping them is configurable. |
| Volume                                  | The desired volume, which will be baked in. For videos, 0 will result in the audio channel being removed. |

## Other notes

If you want the videos to cover the whole screen, try the following CSS:

```css
body:not(.hide-scroll) > .videoPlayerContainer > video {
    object-fit: cover;
}
```
