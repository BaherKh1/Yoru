V8.1 note: branding-only icon/DPI changes retain existing performance and playback code. The tables below are historical V6 measurements. The V8.1 local native playback check is a compatibility smoke check, not a new performance comparison.

YORU V8 note: this is a branding release with a safe data-root migration. The measurements below are historical V6 results, not new V8 benchmarks. Existing streaming/backend behavior and performance optimizations are retained. See V8-CHANGES.md.

V7 note: the results below are the historical V6 performance comparison, not a new V7 benchmark. V7 retains those optimizations and the 0.6.1 player controls. Its changes are episode viewport sizing/pixel scrolling, scrollbar placement, compact episode search and resilient artwork loading. API/backend/player code is unchanged. See V7-CHANGES.md and VALIDATION.md.

# Yoru V6 performance report

V6 keeps the .NET 8 WPF/LibVLCSharp architecture, V5 theme, poster dimensions, hover animations, language support, APIs, resolver, history, and player icon. V5 is the earlier release generation; V6 source was developed outside this repository.

Before changing runtime behavior, an instrumentation-only V5 baseline copy was created for comparison. The same PerformanceDiagnostics and PlaybackPerformanceDiagnostics harnesses run against both versions. Original V5 runtime files are retained in the baseline; only diagnostic entry points and harness files were added.

The browser benchmark uses 5,000 synthetic IDs based on 30 real captured anime records/artwork, and 100 scratch watch-history entries. It uses a hidden 1366x768 WPF HwndSource, 120 scroll iterations, eight incremental 30-item page loads, and five transitions to each section. Each sample starts from another section, not repeated clicks on the active navigation item. There are three fresh-process runs per version, run serially. Values below are medians across those runs. Source requests are fixtures/local captures, not live-server latency.

| Measurement | V5 | V6 | Unit | Reduction |
|---|---:|---:|---|---:|
| Initial 5,000-title setup | 190.28 | 118.23 | ms | 37.9% |
| First Explore render | 199.85 | 457.94 | ms | -129.1% |
| Scroll UI work, p95 | 2.89 | 1.89 | ms | 34.5% |
| Scroll loop interval, p95 | 32.86 | 17.81 | ms | 45.8% |
| Scroll allocations | 64.05 | 12.97 | MiB | 79.7% |
| Append 30 titles to 3,000+, median | 58.21 | 1.00 | ms | 98.3% |
| Eight page-appends allocations | 67.43 | 2.69 | MiB | 96.0% |
| Process private memory after GC | 499.16 | 196.60 | MiB | 60.6% |
| Live managed heap after GC | 17.20 | 17.83 | MiB | -3.6% |
| Repeated model-property reads | 81.38 | 10.53 | ms | 87.1% |
| Software snapshot render | 36.86 | 32.21 | ms | 12.6% |
| Home warm navigation | 23.29 | 1.08 | ms | 95.4% |
| Trending warm navigation | 82.54 | 1.95 | ms | 97.6% |
| Continue Watching warm navigation | 361.55 | 1.01 | ms | 99.7% |
| Genres warm navigation | 49.66 | 5.24 | ms | 89.5% |
| Seasonal warm navigation | 41.42 | 1.93 | ms | 95.3% |
| Explore warm navigation | 36.03 | 2.54 | ms | 93.0% |

A negative reduction means an increase. The slightly larger live managed heap reflects cached metadata/view state; total process private memory still drops substantially because full-resolution artwork and off-screen history controls no longer accumulate. The summary JSON includes run ranges; raw files include allocated bytes, WPF rendering tier, realized rows, decoded-pixel estimates, and composition events.

Scroll-loop intervals include the intentional 16ms scheduling delay. Hidden composition cadence and software RenderTargetBitmap timings are profiling proxies, not physically presented monitor FPS. No claim of a measured 60/120 FPS display rate is made. The source machine reported 16 logical processors, .NET 8.0.7, and WPF rendering tier 2. Native/video-driver/DPI behavior may differ elsewhere.

The measured bottlenecks were full catalog ItemsSource resets at both loading-start and loading-end, nested poster-control recreation despite outer row recycling, repeated JSON parsing/array allocation in model getters, full-resolution image decoding, rebuilding unchanged history on navigation, and nonvirtualized history artwork. V6 addresses those specific paths:

- Catalog rows are updated incrementally; loading indicators do not reset the data source. Recycled rows retain their poster controls and rebind only their models. Continue Watching also virtualizes rows, while its header scrolls as before. Home remains a small fixed list.
- Display-sized poster decoding runs off the dispatcher. It accounts for UniformToFill, actual DPI, and a 25% oversampling margin for sharpness; it never upscales beyond the source resolution. Frozen pixel buffers detach from compressed originals. No bitmap resampling-quality downgrade is applied.
- Artwork has an 80 MiB/192-entry decoded LRU, a 256 MiB compressed disk budget with 14-day expiry, four-worker concurrency, request/decode deduplication, cancellation of obsolete queued posters, corruption recovery, and bounded request timeouts. Cache hits update recycled controls immediately.
- Immutable card metadata is computed once. Browsing sections retain their data, filters, sorting and scroll positions; unchanged history is not reconstructed. Late results cannot append to a different section.
- Client-side search pages are cached for two minutes (32 entries); details for five minutes (64 entries); episode lists for five minutes (16 entries). Shared reads are deduplicated, parse work stays off WPF, details/episodes load concurrently, and Reconnect clears caches. Server lists and signed/resolved media URLs are always fetched fresh.

Playback profiling uses a generated 12-second 1920x1080 30fps H.264/AAC fixture. Each run observes roughly nine seconds including pause, resume and seek. V6 exercises the real serialized native-command queue; V5 uses its direct native commands. Both run with a hidden native video HWND and dummy audio output.

| Version/mode | Startup ms | CPU ms | GPU decoding observed | Lost pictures | Lost audio buffers | Unexpected clock stalls | Pause drift ms |
|---|---:|---:|---|---:|---:|---:|---:|
| V5 / automatic | 656 | 375 | True | 3 | 0 | 0 | 0 |
| V5 / software | 954 | 1734 | False | 3 | 0 | 0 | 0 |
| V6 / automatic | 602 | 453 | True | 3 | 0 | 0 | 0 |
| V6 / software | 955 | 2062 | False | 2 | 0 | 0 | 0 |

The baseline was already using GPU decoding in the local fixture, despite a contradictory global disable flag: the per-player LibVLCSharp setting took precedence. V6 removes that conflict and selects automatic decoding explicitly. The compatibility test confirms software decoding works with no GPU-decoding log. A detected native-player crash selects software compatibility for the following day; normal closes remove the marker. Resolution, codec quality, playback rate and the existing 1500ms network buffer are retained. VLC remains responsible for its media/audio clock.

V6 debounces seek bursts to the newest target and serializes seek/pause/resume/start away from WPF; disposal waits for native commands to finish. Repeated seek keys still accumulate, and late clock updates cannot overwrite a pending UI target. Each stream starts at zero configured audio delay. Local tests show no lost audio buffers, no unexpected clock stalls, and no pause drift, but still report a few lost pictures over startup/seek. These results do not establish a decoding-speed improvement or zero frame drops. CPU differences in a single short native run are not treated as a performance gain.

Physical speaker/display lip-sync, long-session drift, Bluetooth/audio-device latency, and a specific problematic online source were not measured. The user could not identify a reproducible anime/episode/server. Therefore V6 does not claim to repair every source-specific A/V offset or host/network stall. It adds local playback-health.json sampling of native frame/audio/drop/input counters, output cadence and configured audio delay, with advisory classifications for starvation, output/decoder pressure and source discontinuities. Intentional seeks/resumes are marked so their frame discards are not diagnosed as failures. No URLs, tokens, titles or raw native logs are stored.

Regression coverage includes the existing 23 layout/navigation/history/language groups at 1366x768, 1600x900 and 1920x1080 in English/Arabic; nine V6 cache/recycling/navigation/history checks; 26 player-overlay input/timing checks; five icon/native initialization checks; API/resolver fixtures; history/default-language/deletion persistence; API cache cancellation/expiry/eviction; native command ordering/seek coalescing; and decoder crash fallback. Full results are in evidence/. Final distribution startup, player-session transfer and native decode are checked during packaging.

To reproduce: run tests/run-performance.ps1 from a Windows checkout containing the V5 baseline and generated fixture. Generate the fixture with an installed FFmpeg using testsrc2 at 1920x1080:30 and a 48kHz sine track, 12 seconds, libx264 preset veryfast CRF20, AAC160k, faststart. The fixture and benchmark source are retained in tests/. No FFmpeg is required by the application or shipped runtime.

Implementation references: [Microsoft WPF imaging performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-2d-graphics-and-imaging), [BitmapImage aspect-preserving decode](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-use-a-bitmapimage), and [VideoLAN MediaPlayer API](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html). These support the decode/hardware APIs; the before/after numbers come from the local harness, not those documents.

The 0.6.1 player-overlay patch targets an additional measured code-path issue: every mouse event during the previous fade restarted its 140ms animation. Reveal now responds to target-state changes rather than intermediate opacity, shows at least 35% opacity and enables hit testing immediately, and completes a single 75ms fade. Continuous movement only updates the activity timestamp. Hidden progress DPs are not refreshed, completed opacity clocks are removed, and seek-rail hover scales a render transform instead of invalidating layout. Fade-out is 160ms. A 1,000-event burst starts one reveal, and all 26 overlay checks pass. This patch does not change browsing benchmark paths or native decoding/clock policy.
