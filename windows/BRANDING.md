# YORU 2 artwork trial for V8.1

Source: the user's original Yoru2.png (1254x1254), embedded byte-for-byte. BrandArtwork.CreateFull uses a WPF squircle clip to display the full logo without its outer matte. BrandArtwork.CreateSmall takes the original portrait/crescent region at x=200,y=130,width=900,height=680, excluding the YORU wordmark, and fits it proportionally inside a padded dark/purple tile.

No illustration redraw or small-image upscale is used. The app and icon builder share the exact same BrandArtwork factory. Independent PNG sizes: 16,20,24,32,40,48,64,128,256. Windows icon: yoru-v8.1-logo2.ico.

Existing V8.1 DPI behavior, UI dimensions, sharpness settings and storage are unchanged. The desktop shortcut helper uses the new versioned ICO filename and notifies Explorer about the changed shortcut. The previous V8.1 app and icon packages remain available for comparison.
