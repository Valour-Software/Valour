# Design language

This guide describes how Valour looks and why. Read it before adding or changing
anything visual in the client, the public wiki and thread pages, or the website.
The tokens it refers to are defined at the top of
[globals.css](../Valour/Client/wwwroot/css/globals.css), and the default theme values
live in [Theme.cs](../Valour/Sdk/Models/Themes/Theme.cs).

## The idea

Valour's vocabulary is astronomical: communities are planets, the default theme is
"To The Stars", supporters are Stargazers, and the mascot Victor is an astronaut.
The interface follows that vocabulary by treating the screen as a night sky seen
through a clear window.

- The interface itself is the dark between the stars. Surfaces are quiet and flat,
  so that content and people stand out.
- Light means someone is there. Color marks presence, unread activity, mentions,
  the current selection, and the one main action on a screen.
- Each planet has its own world and sky. Both are generated from the planet's
  id, which gives every community a visual identity without asking it to design
  one.

The sections below turn that idea into rules.

## Principles

1. **Dark is the canvas.** Backgrounds are flat surfaces separated by small steps
   in lightness and thin lines. They do not carry gradients, textures, or glows.
2. **Color is signal.** If something is colored, it should be because a person did
   something, something needs attention, or it is the primary action. Decorative
   color dilutes the signals that matter.
3. **Nebulae are atmosphere, never material.** A sky can sit behind empty space. It
   is never applied to a control, a border, or text.
4. **Precise, like an instrument.** Use thin lines instead of shadows, a small set
   of radii, and a monospaced face for numbers that are read and compared.
5. **Warmth comes from people.** Avatars, names, Victor, and plain writing carry
   the personality. Chrome should not try to.
6. **Calm motion.** Things fade and settle. Nothing lifts, bounces, shimmers, or
   pulses unless it represents something live, such as a person speaking.

## Color

### Surfaces

Use the semantic surface tokens in component CSS. They are derived from the theme's
five main colors, so every published theme provides them.

| Token | Theme source | Use |
| --- | --- | --- |
| `--surface-canvas` | `--main-1` | App background, sidebar, member list |
| `--surface-base` | `--main-2` | Window content, modals, menus |
| `--surface-raised` | `--main-3` | Inputs, cards, code blocks, selected rows |
| `--surface-hover` | `--main-4` | Hover state for rows and quiet buttons |
| `--surface-active` | `--main-5` | Pressed state, strongest neutral fill |
| `--surface-overlay` | `--main-2` | Floating layers such as menus and popovers |

Separate surfaces with `--line` (a faint line) or `--line-strong` (for inputs and
controls that need a visible edge). Both are mixed from the theme's tint color.

### Text

| Token | Use |
| --- | --- |
| `--text-primary` | Body text, names, titles |
| `--text-secondary` | Supporting text, inactive navigation |
| `--text-tertiary` | Timestamps, placeholders, labels, disabled text |

### Accent and status colors

`--color-primary` is the accent. Use it for links, the primary button, focus rings,
mentions of the current user, and unread counts. Each accent has `-soft` (tinted
fill), `-border`, and `-contrast` (text on a solid fill) variants. The contrast
color is calculated from the fill, so light accents get dark text.

Status colors are `--color-success`, `--color-warning`, and `--color-danger`. Use
them only for state: online presence, a warning banner, a destructive action.

`--color-bot` marks bots and `--color-staff` marks staff. Role colors come from the
planet and are shown on names only.

### Planet light

`--planet-light` is a light tint of a planet's sky, from
`NebulaPalettes.PlanetLight(planetId)`. It is used only for planet identity on the
public thread and wiki pages, such as the ring around a planet's icon. It defaults
to `--color-primary`.

Selection and focus always use `--color-primary`, including the active channel
indicator, the focused message composer, and the selected wiki page, so that "you
are here" looks the same in every planet.

### What should not be colored

- Borders of ordinary controls, cards, and panels. Use `--line` or `--line-strong`.
- Icons in navigation, unless they show an unread or active state.
- Section headings and labels.
- Backgrounds of whole regions.

## Typography

The interface uses **Instrument Sans**. **JetBrains Mono** is used for code and for
values that people read digit by digit: timestamps, counts, IDs, and security codes.
Both are self-hosted under `wwwroot/css/fonts` with their licenses, because the
content security policy does not allow font hosts.

- Use weights 400 (text), 500 (emphasis in controls), and 600 (names, titles,
  buttons). Reserve 700 for large display text.
- Use `--letter-spacing-display` on headings of `--font-size-2xl` and larger.
- Labels above groups of content use one style: `--font-size-xs`, weight 600,
  uppercase, `--letter-spacing-label`, colored `--text-tertiary`. Do not invent other
  label treatments.
- Buttons and tabs use sentence case. Do not uppercase them.
- Numbers that change or are compared use `font-variant-numeric: tabular-nums`.

Themes can replace the interface font through their font family field. The Outfit
face remains available for themes that name it.

## Shape and depth

| Token | Value | Use |
| --- | --- | --- |
| `--radius-xs` | 3px | Inline code, small badges |
| `--radius-sm` | 5px | Tags, small buttons |
| `--radius-md` | 7px | Buttons, inputs, rows, menu items |
| `--radius-lg` | 10px | Cards, menus, the message composer |
| `--radius-xl` | 14px | Modals and large panels |
| `--radius-full` | 999px | Avatars, pills, counts |

Depth comes from surface steps and lines. Shadows are reserved for layers that float
above the page:

- `--shadow-overlay` for menus, popovers, tooltips, and profile cards.
- `--shadow-modal` for modals.
- `--rim-light` is an inset highlight along the top edge of raised controls. It
  replaces glows.
- `--focus-ring` is the keyboard focus indicator.

Do not use colored shadows, blurred glass (`backdrop-filter`) on ordinary surfaces,
or gradient borders.

## Motion

Use `--duration-fast` (hover and press), `--duration-base` (small state changes),
and `--duration-slow` (panels and modals) with `--ease-standard`. `--ease-spring`
settles without overshooting.

- Hover changes color or fill. It does not move, scale, or lift the element.
- Entering elements fade in, and may shift a few pixels. They do not bounce.
- Anything that animates continuously must represent something live.
- Respect `prefers-reduced-motion` by removing movement and keeping fades short.

## Nebula skies

`NebulaSky` ([component](../Valour/Client/Components/Utility/NebulaSky.razor)) draws
a generated nebula into a canvas. The generator is in
[nebula.js](../Valour/Client/wwwroot/js/nebula.js) and has no dependency on the app,
so the website and public pages can use it too.

### How a sky is made

The generator builds a gas field from layered noise with fine filaments, dust lanes,
and small-scale clumping, and colors it in OKLCH with low saturation. Stars of
different color temperatures and film grain are added at full resolution. The gas
field is computed in a Web Worker at reduced resolution, so it does not block the
interface. If workers are unavailable, it is computed on the main thread in slices
of a few milliseconds.

Skies are deterministic. The `Seed` parameter (a planet's world seed for planet skies)
decides the shape, and the palette decides the colors. Finished skies are cached per
seed, palette, and size for the session, and sizes snap to 64-pixel steps so small
layout changes reuse the cache.

### Palettes

Palettes are named after real nebulae and follow their emission colors loosely:
`carina`, `eagle`, `rosette`, `crab`, `helix`, `veil`, `lagoon`, `orion`, and
`tarantula`. A planet's palette is picked from its seed. The
`valour` palette is reserved for brand surfaces: sign-in, loading, and the website.

### Where skies belong

- Behind the sign-in and account pages and onboarding (brand palette).
- On planet banners, the planet header in the channel list, the planet's About
  page, and discovery cards (the planet's palette).
- On profile cards, seeded by the user's id, unless the user chose a background
  image.
- In scenes with Victor (see [Victor and scenes](#victor-and-scenes)).

A sky must fade out before anything that has to be read, and must never sit behind
dense text or controls without a solid surface in between. Large display text that
overlaps a sky, such as a hero headline, may carry a soft black shadow that acts
as a local scrim. The shadow is never colored and never used on ordinary text. Keep `Intensity` near 1
for atmosphere behind content, and go up to about 1.5 only on hero surfaces.

## Planets

Every community has its own planet. `PlanetGlobe`
([component](../Valour/Client/Components/Utility/PlanetGlobe.razor)) draws it, and the
generator is in [planet.js](../Valour/Client/wwwroot/js/planet.js).

### How a planet is derived

Only one small number about a planet's appearance is stored: its world variant.
`planetGenome` derives everything else from a seed built from the planet's id and
that variant, so the same planet looks the same on every device and no two planets
are alike. `NebulaPalettes.WorldSeed` builds the seed. Variant 0 is the original
world and uses the id alone; other variants append `~` and the variant number, as
in `12215159187308544~3`. Every place that draws a planet, its sky, or its planet
light uses this seed, so they change together.

People with the Manage permission can pick a different world in the planet's Info
settings. Regenerate previews the next variant, cycling from 1 to 255, and Reset to
original returns to variant 0. Nothing changes until they save, which goes through
the ordinary planet update. Open clients receive the change as a planet update and
redraw the globe, sky, and icon.

The seed decides:

- **Kind of world.** One of seven: continents, ocean, desert, gas giant, ice, bare
  rock, or lava. Gas giants and continents are the most common.
- **Colors.** The planet uses the same palette as its sky (see
  [Palettes](#palettes)), turned by an offset taken from the seed, so the planet
  and its sky belong together without planets sharing a color scheme. Hues
  between yellow-green and green are pushed aside because they turn muddy at low
  saturation.
- **Surface.** Continuous traits such as sea level, cloud cover, the number and
  turbulence of gas bands, storms, polar caps, crater density, spin, axial tilt,
  and atmosphere strength.
- **Rings and moons.** Rings are likely on gas giants and rare elsewhere. A planet
  has up to two moons.

The only runtime input is the brightness of the lights on the night side. It shows
how many people are around: `PlanetGlobe.LightsFor` turns a member count and an
active count into a brightness, so busy planets glow and quiet ones stay dark.
The active count is the number of members who connected in the last 15 minutes.
The discovery list reports it as `ActiveCount`, and startup data reports it for
joined planets, which `PlanetService.GetActiveCount` returns. Both use -1 when the
host does not report it (community-hosted planets), and those planets get a
moderate default.

Scenes can adjust a planet with three parameters. `Kind` pins the kind of world,
such as a rock moon for Victor to stand on. `Plain` leaves out rings and moons, for
planets used as ground, a horizon, or an icon. `Light` sets the direction of the
sunlight; lighting from behind leaves a crescent with the night side facing you.

### Rendering

A planet renders into a transparent layer, so it can sit over any sky or image. The
work runs in the same Web Worker as the skies ([sky-jobs.js](../Valour/Client/wwwroot/js/sky-jobs.js)),
and finished planets are cached by seed, radius, and light level. Light levels are
rounded to tenths so that small changes in the active count reuse the cache.

Server-rendered pages and C# code that need a planet's colors, such as its planet
light, use [NebulaPalettes.cs](../Valour/Client/Utility/NebulaPalettes.cs), which
repeats the palette choice in C#. Palette names are internal. The interface never
shows them; labels over a sky carry useful information, such as a planet's topics
or its name. `NebulaPaletteTests` and `nebula.test.mjs` assert the same
values so the two cannot drift apart.

Every planet is bright enough to stand out from its sky. Before rendering, the
generator samples the surface's average luminance, and brightens worlds below a
target (`TARGET_ALBEDO`) until they reach it, up to five times. Worlds that are
already bright keep their own look. Every planet also gets at least a faint
atmosphere rim, so its edge shows against dark skies. `planet.test.mjs` renders
600 planets and checks the lit side of each one.

### Where planets belong

- Discovery cards, where the planet sits in its own sky.
- The planet header in the channel list, and the planet's About and join pages.
- Sign-in, onboarding, and empty states, as part of a scene.
- `PlanetIcon`, when the planet has no custom icon. The icon shows the planet
  without rings or moons, so it stays readable at small sizes. A custom icon
  always takes precedence.

## Lists, people, and activity

Rows for planets, conversations, and people share one layout:

- A round 32-pixel icon or avatar.
- The name in `--text-secondary`, or `--text-primary` at weight 600 when there is
  unread activity.
- One supporting line in `--text-tertiary`. For planets it is the active count in
  mono with a 6-pixel dot (`--color-success` when anyone is active, `--line-strong`
  when the planet is quiet). For conversations and people it is the status text or
  presence.
- On the right, a count pill in `--color-primary` for notifications, or a small
  `--text-primary` dot for unread activity without a count. Timestamps sit on the
  name line in mono.

Presence is a dot at the bottom right of an avatar: success for online, warning
for away, danger for do not disturb, and nothing when offline. Its outline uses
`--avatar-cutout`, which defaults to `--surface-canvas`. A container on another
surface, such as a modal or a hovered row, sets `--avatar-cutout` to its own
background.

Do not draw rings around avatars or planet icons to show unread activity or
presence. Rings compete with the artwork and are hard to read at small sizes.
Where an icon appears without a row, such as in a planet folder, a small dot at
the top right marks unread activity.

## Victor and scenes

Victor is drawn as himself, the dog from the logo, in a few poses: `astronaut`,
`peeking`, `sleeping`, `telescope`, and `rocket`. The illustrations are in
[media/victor](../Valour/Client/wwwroot/media/victor). A new pose is a new
illustration built from his logo shapes. Do not rotate the logo or add props
beside it to suggest a pose.

- Victor keeps his own proportions and markings. Accessories sit on him, such as
  a nightcap on one ear.
- Victor is never white on a near-white background. Place him on the sky or give
  what he sits on a clear contrast.
- Victor appears in moments, not in chrome: empty states, the start of a channel,
  loading, creating a planet, and sign-in.

`SkyMoment` ([component](../Valour/Client/Components/Utility/SkyMoment.razor))
combines a small sky, Victor in a pose, and optional copy. `peeking` places him
behind a planet's horizon, and scenes for a specific planet pass its id as the
seed so the sky and planet match that planet. `telescope` stands him on a rock
moon. Use it for empty and waiting states instead of plain text.

The sign-in page and the account pages (password reset, email verification, age
check, and app authorization) share `SignInShell`
([component](../Valour/Client/Components/Utility/SignInShell.razor)): a brand sky
with the home planet, the Canis Victor constellation, and Victor on a spacewalk,
next to a panel for the form.

The loading screen is a flight home through the same sky. It lives in the host
page ([boot-scene.js](../Valour/Client/wwwroot/js/boot-scene.js)) outside the
Blazor root, so it keeps running from the first paint until the first sign-in
attempt finishes, and then fades out. Stars stream from a vanishing point and slow
down as loading completes, the home planet rises into its place on the sign-in
page, and Victor floats in the middle above a mono line with the progress and a
rotating joke. Progress comes from the `--blazor-load-percentage` value that Blazor
sets while it downloads. The stars are drawn in a worker, and the planet and sky
move with CSS transitions, because the .NET runtime blocks the main thread while it
starts.

Wherever Victor floats (the loading screen, window loaders, sign-in, and
onboarding), he uses the shared `victor-float` animation, so he moves the same way
everywhere.

## Logo and app icon

The app icon is Victor in his two-tone colors in front of the brand nebula. His
bowtie is ink, like his eyes and nose, so it stays readable at small sizes; the
rim, the sky, and props around him carry the color. The icon is an ink squircle with a thin rim that runs from
violet to cyan. The rim is the only place the brand gradient appears, and it has
no glow.

Victor is centered by the center of mass of his silhouette, placed slightly
above the middle, rather than by his bounding box. His face sits on the left of
the logo, so centering the box makes the icon look off center next to text or
other icons.

The icon comes in a few forms:

| Form | Files | Use |
| --- | --- | --- |
| Outline | `media/logo/logo-*` | General logo image, installed web app icon, emails |
| Full bleed | `media/logo/logo-square-*` | Icons that the platform masks: maskable web app icons, iOS home screens, push notifications |
| Small | `favicon-*`, `favicon.ico`, the Windows tray icon | Sizes of 48 pixels and below, without the nebula so Victor stays legible |
| Layers | `Client.Maui/Resources/AppIcon/Android` | Android adaptive icons: the sky as the background and Victor as the foreground, sized to the safe zone |

The icon, social images, and wordmarks are generated by
[Tools/Brand](../Tools/Brand/README.md), and the finished images are kept in
`media/socials`.

Inside the interface, Victor appears as a single-color mark (`Victor.razor`)
next to the wordmark. The wordmark is "Valour" in Instrument Sans at weight 700
with tight letter spacing. Lockups for light and dark backgrounds are in
`media/logo/wide`.

## Themes and compatibility

Published themes set the fields in `ISharedTheme`, and `ThemeComponent` turns them
into CSS variables. Everything in this guide is derived from those fields, so older
themes still render. Keep these constraints in mind:

- The legacy names (`--main-1` through `--main-5`, `--v-*`, `--p-*`,
  `--slight-tint` and friends) stay defined. New CSS uses the semantic names above.
- Theme colors are six-digit hex values. The theme system appends alpha by string
  concatenation, so other formats break the tint and modal variables.
- Custom CSS in themes targets existing class names. Renaming a widely used class
  such as `.sidebar`, `.window`, or `.topbar` breaks published themes.
- The default theme is defined in code with id 0 and is not stored on devices, so
  changes to it reach every user who has not installed another theme.

## Avoid

- Gradients on buttons, text, borders, tabs, toggles, or indicators.
- Colored glows and neon colors.
- Blurred glass on ordinary surfaces.
- Hover lifts, scale on hover, shimmer sweeps, and bouncing easing.
- Uppercase buttons and extra letter-spaced label styles.
- Emoji used as interface icons. Use Bootstrap Icons.
- Hard-coded colors in component CSS. Use the tokens.
