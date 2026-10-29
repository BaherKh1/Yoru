# YORU Android V9.3 ? custom Home artwork / Trending removal (90311)

## Trending removed

Bottom navigation now contains Home, Explore, History and More. The Trending page and its catalog ranking branch are removed. Old restored Trending page/parent names migrate to Explore safely. The chart-shaped rating-sort icon remains available under an internal Chart name; it is not a Trending section. Navigation dimensions/style remain the chosen reference-explore style, with four evenly distributed cells.

## Home-only custom images

| Anime | Original filename | Original dimensions |
|---|---|---|
| One Piece | Onepiece.jpg | 3840 x 2160 |
| Ore dake Level Up na Ken / Solo Leveling | levelup.jpg | 3840 x 2160 |
| Jujutsu Kaisen 2nd Season | JujutsuKaisen S2.jpg | 2000 x 1817 |
| Black Clover | BlackClover.jpg | 3840 x 2160 |
| Jujutsu Kaisen | jujutsukaisen S1.jpg | 1800 x 1012 |

Files are copied unchanged into Android Assets/Featured, without resizing, recompression, drawable-density conversion or metadata changes. The APK's asset bytes are compared directly with the source reference files; hashes/dimensions are recorded in evidence/featured-source-images.json. Runtime decoding uses ARGB8888 and display-appropriate sampling to limit memory; this does not alter the packaged original files. ImageView CenterCrop provides cover scaling without stretching, so edges may be cropped depending on the screen aspect ratio.

FeaturedArtwork maps exact normalized series names and specific season aliases. Season 1 and season 2 are distinct; movies, later seasons and unrelated titles retain their original artwork. The override is called only for the Home carousel's featured row. Explore, details, history, My List and every other poster/banner continue using the existing model URLs. No poster/model URL is overwritten or saved. Existing featured selection, anime metadata, watch/favorite actions and carousel transitions remain unchanged. Packaged artwork loads off the UI thread through the existing bounded image cache with cancellation; failures fall back to the original banner URL.

Application files: FloatingNavigation.cs, MainActivity.cs, StreamIcons.cs, FeedAdapter.cs (rating icon name), CinematicHero.cs, PosterCache.cs (new asset loader only), MobileNavigation.cs, new FeaturedArtwork.cs, Assets/Featured/*.jpg and versionCode 90311. Tests in Program.cs cover the four-tab navigation/migration, geometry, five image mappings, season aliases, non-Home isolation and rejection of other movies/seasons.

Validation: Release build has zero warnings/errors; 15 portable groups pass. Asset byte equality, package/signature checks and Windows/backend/style preservation are recorded under evidence/featured-*. The original network poster loader is unchanged. No device is connected, so real native image rendering/cropping/scrolling still needs hardware verification. Install as an update without uninstalling to preserve data.

Device checks: verify no Trending tab; swipe through the five named Home features and compare their supplied artwork; rotate/check portrait and landscape cover crops; open each title in Explore/details/My List to confirm original artwork remains. Verify watch/favorite actions, More Back behavior and exact Explore return position remain operational, including Arabic.
