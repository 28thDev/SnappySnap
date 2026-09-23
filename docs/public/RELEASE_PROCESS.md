# Release preparation and publication

A release is a verified source revision plus its exact installer and signed update catalog. A successful local build or a version tag alone is not release acceptance.

## Versions and source

`Directory.Build.props` is the version source for the application, helper and installer. Use `MAJOR.MINOR.PATCH`: increment MINOR for backward-compatible features, PATCH for fixes and MAJOR for an explicitly accepted breaking change. Documentation, artwork and build-only changes that do not alter shipped behavior do not require a bump.

Use numeric versions without prerelease suffixes. Never replace a distributed version with different installer bytes or move its tag. Fix a published build with a new version.

`master` is the permanent development branch. Use temporary feature/fix branches for larger changes. A temporary `release/<version>` branch is only needed to stabilize a release alongside newer development. Tags use `v<version>` and point to the exact accepted commit.

## Candidate checks

1. Review the source, public documentation, screenshots, dependency notices and license. Public screenshots must show the actual application with safe demonstration content.
2. Run locked restore, a full Release build and tests, public-tree validation, release-tool tests and Windows CI. Resolve failures before packaging.
3. Exercise screenshot → editor → save/copy/drag, and recording → pause/audio toggles → stop → edit/export on Windows. Record the tested displays, scaling and audio devices. Automated rendering does not replace physical input or listening.
4. In a disposable Windows profile, verify installation, update, repair, uninstall/reinstall, retained settings/history/media and startup preferences.
5. Verify the signed GitHub update path with an older installed candidate and a newer release: check, download, install, restart, retained data and offline cached installation. Confirm cancellation and rejection of damaged packages.
6. Confirm recoverable custody of the update signing key, the actual Authenticode signing status and a working private security-reporting channel. Catalog signing does not establish a Windows publisher.
7. Finalize release notes with tested behavior, known limitations and accurate installation/update instructions. Keep preparation notices until the release is actually available.

## Build and stage

Start from a clean, committed candidate. Replace `<version>` and `<build-id>` below with that candidate's values.

```powershell
dotnet restore SnappySnap.sln --locked-mode
./build.ps1 -Configuration Release
./test.ps1 -Configuration Release -NoBuild
./tools/release/Test-PublicTree.ps1 -RequireLicense
./build-installer.ps1
```

Retain the build log and `.exe.build.json` sidecar locally. Use the fresh staging path printed by the build when signing the catalog:

```powershell
./installer/publish-release.ps1 `
  -InstallerPath ./artifacts/installer/SnappySnap-Setup-<version>-x64.exe `
  -PublishedAppPath ./artifacts/installer-staging/<build-id>/SnappySnap.exe `
  -NotesPath ./docs/public/releases/<version>.md `
  -Destination ./artifacts/signed/<version>

./tools/release/Stage-Release.ps1 `
  -InstallerPath ./artifacts/installer/SnappySnap-Setup-<version>-x64.exe `
  -SignedCatalogDirectory ./artifacts/signed/<version> `
  -NotesPath ./docs/public/releases/<version>.md `
  -OutputDirectory ./artifacts/release-ready/<version>
```

Use fresh output directories. Staging validates clean-source provenance, installer hash/version, signed catalog and final notes. Upload only the six staged files:

- `SnappySnap-Setup-<version>-x64.exe`
- `latest.json`
- `latest.sig`
- `SHA256SUMS.txt`
- `RELEASE_NOTES.md`
- `release-manifest.json`

Never upload private keys, build sidecars, logs, profiles, test media or whole staging directories. SHA-256 identifies the installer bytes; the signed catalog authenticates them to existing installations.

## Publish and verify

After acceptance, create a local annotated tag with `tools/release/New-ReleaseTag.ps1 -Version <version>`. The helper validates the branch, version, source tree and license. Push only the intended branch and tag in the authorized release session.

Create a draft GitHub Release and attach all six staged assets. Check its commit, notes and signing status before publication. With immutable releases, all assets must be complete before publishing. If the candidate changes, rebuild and repeat acceptance before tagging or releasing it.

The updater reads the official repository's unauthenticated `releases/latest` API and stable numeric tags. Drafts and releases marked **pre-release** are not update candidates. A private repository cannot supply updates to this client without changing the transport; do not embed a GitHub token in the app. An end-to-end validation run therefore needs a publicly readable release source and explicit approval to expose its contents.

After publication, verify the latest-release response, signed catalog and installer download from the exact tag. Compare the downloaded bytes with the accepted package, then verify check/update behavior in a disposable profile. Preserve evidence; never silently replace a published binary.

## Source exports

`tools/release/Export-PublicSource.ps1 -OutputDirectory <new-directory>` exports the committed allowlisted tree without Git history. Review the result and run `Test-PublicTree.ps1 -Directory <export> -RequireLicense`. The scan is a bounded check, not a complete secret or artwork audit.

When establishing a clean public history, initialize it from the approved export and verify it again. Preserve private development history separately; do not merge it into the clean public history. The public release tag and package provenance must agree with the final source commit.
