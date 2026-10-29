# Anime Witcher V7 (0.7.1)

Implemented:

- Fixed details layout (0.7.1): removed the outer details ScrollViewer and vertical StackPanel. A four-row Grid holds Back, the fixed anime hero, the fixed Episodes/count/search row, and a star-sized episode viewport. The list fills the remaining height without a manual MaxHeight calculation. Its own thin purple scrollbar is visible; no main details scrollbar exists. Independent pixel scrolling, virtualization and bottom breathing room keep the final episode accessible. Episode row template, colors, padding and spacing are unchanged.
- Responsive upper section: the banner retains its standard 320px height at normal sizes and adapts to 270px in the minimum 1000x600 window. Poster, two-line title, metadata, synopsis and action buttons remain fixed and in bounds. The text grid reserves space for actions and uses complete synopsis lines. English/Arabic, RTL and resize transitions are covered by hidden rendering checks.
- Episode search: 240x36 dark rounded shell, 15px search icon, localized English/Arabic placeholder, visible text/caret and responsive existing filtering. Vertically centered alongside the Episodes heading.
- Main scrollbars: reusable two-column ScrollViewer template reserves a separate right-edge gutter. Thin 7px rounded purple thumb with hover/drag states. Content keeps its own RTL/LTR direction. The episode list uses this thin scrollbar; its outer details page has none. Catalog infinite scrolling and row virtualization stay enabled.
- Artwork: cached ordered fallback URLs from each real record for posters, banners and history; high-resolution poster fallback for missing banners; MAL JPEG alternatives for WebP records. Existing record trailer thumbnail is the last resort when every poster/cover is dead. A fallback can display after a 150ms grace period while a slow preferred source continues; a successful preferred source replaces it. Recycled controls cancel obsolete work. Failed visible artwork retries with backoff; hidden/unloaded controls stop retry timers.
- Cache: rejects HTML/JSON/error documents and undecodable image payloads before persistence. Corrupted disk entries are removed and refetched during the same load. Existing memory/disk budgets, cancellation, deduplication, four-worker limit and display-sized decode stay intact.

Modified source:

- MainWindow.xaml / MainWindow.xaml.cs: fixed details grid, responsive hero content, episode viewport and scroll reset; retained search and artwork bindings.
- Themes/Dark.xaml: shared page viewport and thin scrollbar templates.
- Presentation.cs: same-anime ordered artwork alternatives and cached model properties.
- Controls/RoundedImage.cs: progressive fallback, rebind/cancellation protection, bounded visible retries.
- Controls/AnimeCard.xaml / Controls/ContinueCard.xaml: fallback bindings; card designs unchanged.
- ArtworkCache.cs: payload validation, corruption recovery and dimension bounds.
- Localization/strings.json, AnimeWitcherDesktop.csproj: new translated placeholder and V7 version.
- App.xaml.cs / V7Diagnostics.cs: hidden regression entry point and checks.
- README.md, VALIDATION.md, PERFORMANCE.md, build.ps1: release documentation and packaging.

Architecture, backend APIs, resolver, history storage, playback engine and V6 player behavior are retained. Existing local data uses the same directory. Read VALIDATION.md for test coverage and source availability limits.
