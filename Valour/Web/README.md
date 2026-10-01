# Public website

`Valour.Web` contains the public website and its static exporter. The exporter
renders MVC pages and copies `wwwroot` assets into a directory suitable for
Cloudflare Pages.

From this directory, run:

```sh
dotnet run -- export
```

The output is `dist/`, including rendered pages, static assets, `sitemap.xml`, and
`_redirects`. Use the repository's pinned .NET SDK for local builds.

## Invite and planet links

The app shares invite and planet links on the root domain, such as
`https://valour.gg/i/{code}`, because they are shorter than the app's own
address. `_redirects` sends `/i/*`, `/d/*` and `/planet/*` to the same path on
`app.valour.gg` with a 302. Browsers keep the link's fragment, which carries the
key of a signed invite to a private planet. Apps that build link previews follow
the redirect and read the app page, which describes the planet.

## Policy pages

The privacy policy, terms of service, platform rules, and child safety standards
are written as files at the repository root (`PRIVACY`, `TERMS_OF_SERVICE.md`,
`PLATFORM_RULES.md`, `PLATFORM_ECO_RULES.md`, and `CHILD_SAFETY.md`). The build
copies them next to the site binaries, and `LegalDocumentLibrary` renders them as
Markdown into `/privacy/`, `/terms/`, `/rules/`, `/rules/economy/`, and
`/child-safety/`. Google Play links to `/child-safety/` as Valour's published
standards against child sexual abuse and exploitation. Links between these files, written as file
names or GitHub URLs, point to the matching site pages. Edit the root files, not
the site, to change a policy.

`/delete-account/` is a Razor view (`Views/Home/DeleteAccount.cshtml`) that
explains how to delete an account and what is kept afterwards. Google Play links
to it from the app listing, so keep it in step with `UserService.HardDelete`.

## Skies, planets and fonts

The homepage draws its nebula skies and planets with the same generators the app
uses. `wwwroot/js/sky/` holds copies of `nebula.js`, `planet.js`, `sky-jobs.js` and
`sky-worker.js` from `Valour/Client/wwwroot/js/`, and `wwwroot/js/sky.js` renders
every canvas that has a `data-sky` or `data-planet` attribute. The Instrument Sans,
JetBrains Mono and Bootstrap Icons fonts in `wwwroot/css/fonts/` and the Victor
illustrations in `wwwroot/media/victor/` are also copies from the client. The site
is exported as static files, so copy these again after changing the originals.

## Discord comparison

`/discord-alternative/` (`Views/Home/DiscordAlternative.cshtml`) compares Valour
with Discord and explains how to move a server. The Discord column describes
another company's product at a point in time, and the page shows when it was
last checked. Review the table, the "Before you move" list, and the date whenever
Discord or Valour changes encryption, age checks, ads, pricing, or importing.

## Cloudflare Pages

The website's Pages project uses these settings:

| Setting | Value |
| --- | --- |
| Production branch | `main` |
| Root directory | `Valour/Web` |
| Build command | `sh ./cf-build` |
| Output directory | `dist` |

`cf-build` installs the expected SDK when needed, performs a Release export, and
checks that required output files exist. `VALOUR_WEB_BASE_URL` sets the canonical
site URL and defaults to `https://valour.gg`. Attach `valour.gg` and `www.valour.gg`
to the website's Pages project when configuring its domains.

The application has its own Pages configuration at the repository root, using
the root `cf-build` script and `output` directory. Website and application builds
have separate roots and output directories.
