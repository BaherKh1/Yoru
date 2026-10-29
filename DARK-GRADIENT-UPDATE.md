# YORU Android V9.3 ? dark gradient glass (90307)

Removed the milky-white/lavender wash introduced in 90306. Re-examined bar.png and sampled unlabelled surface pixels: the reference ranges from muted purple/blue-gray around the left side to near-neutral charcoal in the center and darker violet at the right.

FloatingNavigation now layers a horizontal smoky-purple/charcoal translucent gradient over the stronger blurred backdrop. The border is subdued rather than pale white, the shadow is softer, and the selected pill has a muted purple gradient closer to the reference. There is no white overlay. The floating dimensions, corners, icons, label spacing, animation timing and Home/Explore behavior are unchanged. No scroll/idle restyling or recurring blur capture is added. The retained frost is a stationary texture over live content visible through the layers, not continuously sampled live blur.

Application source changes are isolated to FloatingNavigation.cs and versionCode 90307. All other Android source/assets and Windows files are checked against the prior package. Release compilation/signature/package/ZIP checks are recorded under evidence/dark-gradient-*. Core tests were not rerun for this isolated styling change.

No device is connected; pixel-exact matching and actual rendered results are not verified. Install this update over the existing app to preserve data. Check the bar against the reference over several posters, at rest and while scrolling, in English/Arabic and portrait/landscape.
