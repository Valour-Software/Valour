# Valour.Web

The public marketing website for Valour.

## Static export

Run the exporter from this directory:

```bash
dotnet run -- export
```

The generated Cloudflare Pages site is written to `dist/`. It includes the rendered MVC pages, `wwwroot` assets, `sitemap.xml`, and `_redirects`.

For a production-equivalent Pages build:

```bash
sh ./cf-build
```

## Cloudflare Pages project

Create a second Pages project connected to the same Git repository as the Valour app project, with these independent settings:

- Production branch: `main`
- Root directory: `Valour/Web`
- Build command: `sh ./cf-build`
- Build output directory: `dist`

The existing app Pages project remains rooted at the repository root and continues to use the root `cf-build` script and `output` directory.

After the first successful marketing deployment, attach `valour.gg` and `www.valour.gg` to this Pages project. The canonical site URL defaults to `https://valour.gg`; it can be overridden with the `VALOUR_WEB_BASE_URL` Pages environment variable.
