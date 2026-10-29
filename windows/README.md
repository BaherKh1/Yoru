# YORU Desktop V9.3

Extract the complete YORU-V9.3.zip and run its YORU.exe. Keep DLLs, Assets, certs and vlc beside the executable. Windows x64 and the .NET 8 Desktop Runtime are required. Version: 0.9.3.

The sidebar uses one persistent purple indicator with a 250ms CubicEase EaseInOut slide, plus 180ms icon/text selection and hover transitions. Unchanged layout and repeated Loaded notifications do not reset the slide. Rapid navigation replaces the current motion from its displayed position. All sections share the indicator across the visual divider. Anime details keep their originating section selected. OS-disabled client-area animations are respected.

Featured titles wrap to two complete lines without cutting off the lower line; exceptionally long titles use ellipsis. Existing responsive font sizes and poster geometry remain. V9.1's custom #090C14 player titlebar, window controls and compact year/category badges are retained.

Home reads its last successful catalog snapshot asynchronously while creating the window, then refreshes in the background. A launch without cached metadata shows lightweight skeletons. Cached artwork can paint without waiting for HTTP; equivalent responses avoid rebuilding Home controls and offline refresh retains cached content. home-catalog-v9.json uses the existing YORU data directory. STARTUP.md records the original startup measurements.

The dark/purple theme, Yoru2 artwork, English/Arabic and RTL/LTR, search/filter/sort, infinite scrolling, bookmarks, Continue Watching management, fixed details header/scrollable episode list, streaming and player controls are retained.

Data lives in %LOCALAPPDATA%/YORU. Existing settings, favorites, history, deletion markers and playback progress remain accessible. The existing safe legacy migration from AnimeWitcherDesktop is unchanged. No data reset is needed.

To create or update a desktop shortcut after extracting to a permanent folder, run Create-Desktop-Shortcut.ps1. It points to YORU.exe beside the script. Existing shortcuts were not changed by this update.

See V9.3-CHANGES.md for the maximize fix and modified files, V9.2-CHANGES.md for prior sidebar/title fixes, and VALIDATION.md for checks. BRANDING.md and THIRD-PARTY.md document artwork and bundled components. STARTUP.md and PERFORMANCE.md retain historical measurements rather than new performance claims. Source was developed and packaged outside this repository. Earlier sources and ZIP packages remain preserved.

V9.3 fixes player maximize bounds using the current monitor working area, keeping the Windows taskbar outside the video and overlay. The fixed 7px maximize inset is removed. Fullscreen still fills the entire monitor and restores the previous normal/maximized mode. All prior V9.2 sidebar/title and V9.1 player/card fixes remain. V9.2 source and ZIP are preserved.
