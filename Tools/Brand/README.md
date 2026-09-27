# Brand assets

This folder generates Valour's app icon, social images, and wordmarks. The
finished images are kept in [media/socials](../../media/socials). The rules behind
them are in [the design language](../../Docs/DesignLanguage.md#logo-and-app-icon).

The pages draw with the same sources as the app: the nebula and planet generators
in `Valour/Client/wwwroot/js`, the fonts in `Valour/Client/wwwroot/css/fonts`, and
Victor's illustrations in `Valour/Client/wwwroot/media/victor`. `victor-parts.json`
holds the shapes of the Victor logo (face, far side of the face, eyes, nose, body,
and bowtie), so the icon can color each part.

## Pages

| Page | Draws |
| --- | --- |
| `icon.html?v=outline` | The app icon: squircle, nebula, Victor, and the gradient rim |
| `icon.html?v=bleed` | The full-bleed icon for platforms that apply their own mask |
| `icon.html?v=small` | The favicon and tray icon, without the nebula |
| `icon.html?v=bg` | The sky layer of the Android adaptive icon |
| `icon.html?v=avatar&k=1.8` | The social profile picture, with Victor larger |
| `card.html` | The 1200 by 630 link preview image and the wordmark lockups |
| `header.html` | The 1500 by 500 Twitter header. Add `?mock` to preview the profile picture on top |
| `splash.html` | Victor for the native splash screen |

## Rendering

1. Render the masters into `media/socials`. This needs Playwright with Chromium.
   Set `PLAYWRIGHT_MODULE` to the module path if Node cannot resolve `playwright`.

   ```sh
   node Tools/Brand/render.mjs
   ```

2. Write the sized files used by the app, the website, and the native app. This
   needs Pillow.

   ```sh
   python3 Tools/Brand/export.py
   ```

The export replaces the logos and favicons in `Valour/Client/wwwroot`, the logos,
favicon, and link preview in `Valour/Web/wwwroot`, and the icons, Android layers,
tray icon, and splash screen in `Valour/Client.Maui`. The Android build caches
generated icons, so delete `Valour/Client.Maui/obj/*/net*-android/resizetizer`
before rebuilding to see a changed icon.

## Files in media/socials

| File | Use |
| --- | --- |
| `icon-outline.png` | Master for the app icon and logo images |
| `icon-full-bleed.png` | Master for masked icons |
| `icon-small.png` | Master for favicons and the tray icon |
| `icon-android-background.png` | Android adaptive icon background |
| `social-avatar.png`, `social-avatar-400.png` | Profile picture for social accounts |
| `social-card.png` | Link preview image, also published as the website's `twitter-card.png` |
| `twitter-header.png` | Twitter header |
| `wordmark-dark.png`, `wordmark-light.png` | Icon and name for dark and light backgrounds |
| `splash-victor.png` | Native splash screen |
