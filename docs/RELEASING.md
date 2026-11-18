# Releasing a version

Pushing a version tag does the work: GitHub Actions tests the code, builds the installer and publishes the release,
and installed copies update themselves.

## Steps

1. **Write the notes.** Add a section to the top of [CHANGELOG.md](../CHANGELOG.md):
   ```markdown
   ## 0.2.0

   - What changed, in words a user understands.
   ```
   That section becomes the release notes on GitHub and in the update.
2. **Bump the version** in [Directory.Build.props](../Directory.Build.props): `<Version>0.2.0</Version>`.
3. **Commit and push**, and wait for the [CI workflow](../.github/workflows/ci.yml) to pass.
4. **Tag and push the tag:**
   ```bash
   git tag -a v0.2.0 -m "Zoomies 0.2.0"
   git push origin v0.2.0
   ```
   Tags are signed with the maintainer's GPG key; `git verify-tag v0.2.0` should print "Good signature".
5. **Wait for the [release workflow](../.github/workflows/release.yml)** (about 5 minutes). It publishes:
   - `Zoomies-win-Setup.exe` — the installer people download;
   - `Zoomies-win-Portable.zip` — runs without installing;
   - `Zoomies-<version>-full.nupkg`, `Zoomies-<version>-delta.nupkg` and `releases.win.json` — what installed copies
     download to update. The delta is usually well under 1 MB.
6. **Check** the [latest release](https://github.com/lakshmang007/zoomies/releases/latest) has those files and the
   notes. Installed copies pick the update up within a day and apply it on their next start.

## Building the installer locally

```bash
dotnet tool install -g vpk --version 1.2.161
powershell -ExecutionPolicy Bypass -File build\package.ps1 -Version 0.2.0
```

The output is in `artifacts\releases`. Keep the `vpk` version the same as the `Velopack` package in
`src/Zoomies/Zoomies.csproj`.

## If something goes wrong

| Problem | Fix |
|---|---|
| `gpg: can't connect to the keyboxd` when tagging | Start GnuPG's services: `gpgconf --launch keyboxd` and `gpgconf --launch gpg-agent`, then tag again. |
| `gpg: ... expired` when tagging | The signing key has expired: renew it (`gpg --quick-set-expire <fingerprint> 2y`) or create a new one and add it to GitHub. |
| The release workflow fails at "Test" | Fix the failing test on `main`, delete the tag (`git push origin :refs/tags/v0.2.0` and `git tag -d v0.2.0`), then tag again. |
| No delta package | Normal for the first release; every later release gets one. |
