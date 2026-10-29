# YORU V9 (0.9.0) startup fix

Root cause: the original Home view displayed live controls with a null Featured model before Loaded finished settings initialization and the first search. Catalog metadata lived only in process memory. Each launch therefore exposed empty title/description/bookmark content while two service requests completed. A first-load hero/page fade then added unnecessary reveal work.

Changes:

- StartupCatalogStore persists a bounded, schema-validated first-page metadata snapshot in the existing YORU user-data directory. Read/parse and atomic save operations run away from the dispatcher. Corrupt/incompatible files fall back safely; history, favorites and credentials are not part of this snapshot.
- The read starts alongside XAML initialization. A ready warm snapshot is adopted before Show/first paint; otherwise it is applied as soon as the async read finishes. One independent lifetime-bound startup task refreshes settings/catalog in the background and shares existing API initialization/search caching with early navigation.
- Static skeletons occupy the existing hero and eight Popular poster slots until metadata is ready. Empty live buttons remain hidden. Local Continue Watching still renders independently. No production loading delay, splash screen, spinner loop or animated skeleton timer is added.
- Existing cached hero/poster files can be decoded in the background while XAML is created, using the same four-worker/memory-budgeted artwork cache. This priming never makes HTTP requests. Ready full-resolution or sufficiently large bitmaps can paint during initial layout; cached fallback artwork can paint immediately while its preferred source refreshes.
- Equivalent refreshes preserve model/card identity and skip Home rebinding. Changed records reuse unaffected models, preserve the featured selection and do not replace active search/filter/scroll/page state. Identical fallback URL arrays no longer restart image loading. The first Home commit skips the carousel fade; user navigation/carousel animations remain.
- Early cached Home actions share API initialization; close cancels startup consumers. Offline refresh keeps last-known-good data; cold errors display an error state instead of invalid live actions.

Modified: MainWindow.xaml/.cs, StartupCatalogStore.cs, ArtworkCache.cs, Controls/RoundedImage.cs, startup/profile/cache tests, localized loading/status strings, version/build/shortcut asset references and release documentation. Backend request/resolution logic, playback implementation, history storage, theme, poster geometry and latest Yoru2 artwork are retained.

See STARTUP.md and evidence/v9-startup-summary.json for measurements and limitations.
