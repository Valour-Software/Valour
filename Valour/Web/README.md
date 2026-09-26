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

## Policy pages

The privacy policy, terms of service, and platform rules are written as files at
the repository root (`PRIVACY`, `TERMS_OF_SERVICE.md`, `PLATFORM_RULES.md`, and
`PLATFORM_ECO_RULES.md`). The build copies them next to the site binaries, and
`LegalDocumentLibrary` renders them as Markdown into `/privacy/`, `/terms/`,
`/rules/`, and `/rules/economy/`. Links between these files, written as file
names or GitHub URLs, point to the matching site pages. Edit the root files, not
the site, to change a policy.

`/delete-account/` is a Razor view (`Views/Home/DeleteAccount.cshtml`) that
explains how to delete an account and what is kept afterwards. Google Play links
to it from the app listing, so keep it in step with `UserService.HardDelete`.

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
