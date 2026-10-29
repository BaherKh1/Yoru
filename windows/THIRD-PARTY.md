# Bundled playback components

- LibVLCSharp and LibVLCSharp.WPF 3.10.2, VideoLAN, LGPL-2.1-or-later. Official source: https://code.videolan.org/videolan/LibVLCSharp (NuGet package source revision `3082c09ca955131c705fc4767fde03e9c4a14999`). Managed assemblies are shipped separately and can be replaced.
- VLC / LibVLC 3.0.24 x64, VideoLAN. Native binaries and plugins are copied unmodified from the installed VLC distribution; license and credits are in `vlc/COPYING.txt`, `vlc/AUTHORS.txt`, and `vlc/THANKS.txt`. VLC and plugins include components under GPL/LGPL and other licenses. Official corresponding source archives: https://download.videolan.org/pub/videolan/vlc/3.0.24/ ; source repository: https://code.videolan.org/videolan/vlc .

This local preview is provided with source and replaceable libraries. Follow upstream redistribution requirements before distributing a release to others. No video content is bundled.

`certs/mozilla-ca.pem` is the Mozilla CA bundle supplied by https://curl.se/docs/caextract.html (MPL-2.0). It supplements operating-system roots for VLC HTTPS certificate verification; it does not disable certificate or hostname checks. Refresh it from the official source when updating the app.
