# YORU V1.0 validation

Windows x64 / .NET 8 WPF / LibVLCSharp.WPF 3.10.2 / bundled VLC 3.0.24. Final Release 1.0.0 publish completed with zero warnings/errors.

112 UI/regression groups passed on the final executable:

- --v93-checks evidence: 5 groups. Sixty synthetic monitor/work-area cases cover bottom/top/left/right taskbars, zero/positive/negative monitor origins, and 100/125/150/200/250% native-pixel dimensions. The real native WM_GETMINMAXINFO path retains WPF minimum-size tracking. The actual maximized player/frame, LibVLC foreground overlay and controls fit the current monitor work area. Custom maximize/restore, native SC_MAXIMIZE and titlebar double-click restore/maximize behave correctly and preserve the original normal rectangle. Fullscreen fills the monitor and restores safe maximized bounds on exit.
- --v92-checks evidence: 10 persistent-sidebar/rapid-navigation/layout/RTL groups, including all 56 ordered section pairs.
- --v91-checks evidence: 8 player chrome, actual local H.264/AAC decode, window-state/fullscreen and compact badge groups; fixed-inset assertion updated for native bounds handling.
- --design-smoke evidence: 24 responsive layout/interaction groups including featured long titles, original poster geometry, search/filter/sort/bookmarks/history, infinite scrolling and navigation.
- --v9-checks evidence: 7 startup caching/skeleton/offline/cancellation groups.
- --overlay-smoke: 26 video-control groups.
- --icon-smoke: 5 icon/native-player initialization groups.
- --v8-checks evidence: 6 branding/metadata/storage compatibility groups, updated for version 1.0.0.
- --v7-checks evidence: 12 fixed-details/episode-scroll/image groups.
- --v6-checks evidence: 9 cache/recycling/navigation/history/playback-health groups.

--ui-smoke passed. dotnet run --project tests/ApiChecks.csproj -c Release --configfile NuGet.Config passed API/model/resolver, storage migration/deletion, localization, startup/API caching, seek ordering/coalescing and decoder fallback fixtures.

Native before/after measurements on the current desktop (1920x1080 monitor, 1920x1050 work area):

- Before: player rectangle -7,-7 to 1927,1087; content and LibVLC foreground overlay extended to y=1080; controls ended at y=1058, inside the reserved taskbar strip.
- After: player/content rectangle 0,0 to 1920,1050; overlay 0,48 to 1920,1050; controls end at y=1028, above the taskbar. Fullscreen remains 0,0 to 1920,1080. Fullscreen exit restores 0,0 to 1920,1050.
- Normal restore returns exactly to the original 80,80 to 1180,800 rectangle in the diagnostic. Actual WPF minimum-size tracking remains at least the existing 700x460 values.

Tests cloak their native diagnostic player window to avoid covering the desktop. They measure real native/overlay rectangles and local fixture decoding. Different taskbar edges, monitor origins and DPI dimensions are synthetic geometry cases; physical mixed-monitor moves/DPI transitions and live streaming sources were not retested. Desktop/taskbar settings and the System-DPI manifest were not changed.

Twenty-three API/player-overlay/card/main-UI/sidebar/cache/storage/theme/artwork files remain byte-identical to V9.2. Only player window bounds, related diagnostics, version metadata and release docs changed. Existing user data and desktop shortcut are preserved; prior ZIPs remain separate.

Evidence: v93-window-before.json, v93-window-checks.json, v93-test-summary.json and v93-preserved-code.json under the source evidence directory. The separate YORU-Windows-V1.0.zip is checked for required runtime files and CRC integrity. STARTUP.md and PERFORMANCE.md retain historical measurements.
