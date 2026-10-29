# YORU Android V1.0 ? whitened glass (90306)

The bottom navigation now uses a less transparent dark-lavender base, a permanent white/lavender gradient wash, a pale glass border and stronger soft blur (14dp on Android 12+, radius 3 on the downsampled older-Android fallback). The frost opacity increases from 38% to 50%. These layers are constant during scrolling; there is no new per-frame capture or scroll-triggered restyling.

Only FloatingNavigation.cs and the package version code change in application source. Pill shape, icon colors/sizes, purple indicator, Home/Explore scrolling, episodes, streaming and persistent data remain unchanged. Install over the existing app.

Release build/signature/package checks are recorded in evidence/milky-glass-*. This is an isolated visual adjustment; core tests were not rerun. No Android device is connected, so the whitened appearance still needs phone verification.
