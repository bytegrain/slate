# Publishing and consuming packages

Slate publishes its .NET packages privately to `bytegrain` GitHub Packages and its Web package publicly to the
`bytegrain` npm organization:

| Package | Registry identifier |
|---|---|
| Web | `@bytegrain/slate-web` (public npmjs.com package) |
| .NET | `Slate.Core`, `Slate.Blazor`, `Slate.Wpf`, `Slate.Avalonia` (private NuGet packages) |

## Publishing

Push a semantic-version tag to publish all five packages, for example:

```bash
git tag v0.1.0-preview.1
git push origin v0.1.0-preview.1
```

Both publish jobs run only after the .NET, Web, and browser CI jobs pass. NuGet publishing uses the workflow's
`GITHUB_TOKEN` with `packages: write`; no personal token is stored in repository secrets. npm publishing uses
OIDC trusted publishing, so it does not need a long-lived token in GitHub secrets. Since npm configures trusted
publishers on an existing package, bootstrap the initial `0.1.0-preview` npm version once from an authenticated
maintainer account (build first, then publish):

```bash
npm run build
npm publish --workspace @bytegrain/slate-web --access public --tag preview
```

If npm enforces publish-time two-factor authentication, run the publish command in an interactive terminal with
the current authenticator code (`--otp=<code>`); don't store or share the code.

Then configure the package's **Trusted publisher** to use GitHub Actions with owner
`bytegrain`, repository `slate`, workflow filename `ci.yml`, and no environment. Allow direct `npm publish`
and dist-tag management (the workflow uses the `preview` dist-tag for prereleases). npm requires the first
successful OIDC publish within two days of creating the trusted publisher; use a new version such as
`v0.1.0-preview.1` for that first workflow release. The tag supplies the package version.
Prerelease versions (tags containing `-`) use the npm `preview` dist-tag; stable versions use `latest`.
Re-pushing a version skips already-published NuGet packages and the npm job skips a version already present;
published versions are immutable, so use a new version for a republish.

npm automatically creates provenance for supported public-source publishing, but npm currently does not
generate provenance for packages published from private source repositories.

NuGet packages published from this private repository are private by default. Consumers need access to the
GitHub repository/package and NuGet registry credentials with package-read permission. The npm package is
public and does not require credentials to install. Do not commit registry tokens or credential files.

## Restoring packages

Install the public Web package directly from npmjs.com:

```bash
npm install @bytegrain/slate-web
```

For NuGet, add `https://nuget.pkg.github.com/bytegrain/index.json` as a source and authenticate with a
personal access token (classic) with `read:packages`. In GitHub Actions, use `GITHUB_TOKEN` instead when the
consuming repository has been granted access to the package.

Private NuGet GitHub Packages use the storage and data-transfer allowance of the owning account; see
[GitHub Packages billing](https://docs.github.com/en/billing/managing-billing-for-github-packages/about-billing-for-github-packages).
