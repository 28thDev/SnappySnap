# Release preparation and publication

A release is a verified source revision plus its exact installer and signed update catalog. A successful local build or a version tag alone is not release acceptance.

## Versions and source

`Directory.Build.props` is the only version source for the application, helper and installer. SnappySnap uses `MAJOR.MINOR.PATCH` as a product release policy, rather than treating every added control as a SemVer MINOR feature:

| Increment | Choose it when | Examples |
| --- | --- | --- |
| PATCH | The change improves an existing capture, recording, editor, Shelf or settings workflow, preserves user data and keeps prior capabilities accessible. This includes small new controls, entry points or a changed default gesture with an equivalent path to the prior action. | A hotkey for the existing screenshot flow, right-click cancellation, annotation defaults, a more accurate browser region, opening the existing editor from Shelf while retaining Open for the default app. |
| MINOR | The release introduces a new independently useful product workflow or a substantial new capability with its own acceptance plan, while remaining compatible with existing installations. | The first recording workflow or a new media-editing mode. |
| MAJOR | An approved change removes an existing capability without an equivalent path or requires an incompatible data/settings, installer or updater transition. | An upgrade requiring manual migration or loss of compatibility with older captures. |

Size or number of commits alone does not determine the increment. For a mixed batch, use the highest applicable category. Documentation, tests, artwork or build changes with no change in shipped behavior need no bump. If the category is unclear, document the affected workflow and compatibility before choosing; prefer PATCH for a contained improvement to an existing workflow. Record the choice in the changelog or candidate notes.

For each candidate, decide in this order:

1. Check `Directory.Build.props`, existing tags and the last installer handed to testers. A candidate may exist even without a GitHub release.
2. Ask whether shipped behavior changes. If it does not, keep the current version and do not build a replacement installer solely for documentation.
3. If the change is incompatible, seek approval for MAJOR. Otherwise choose MINOR only for an independent new workflow or substantial capability; choose PATCH for a contained improvement to an existing workflow.
4. Update the changelog and candidate notes with the selected number and rationale. Commit the versioned source before building into a fresh output path.

Use numeric versions without prerelease suffixes. Assign the next unused version before handing an installer to a tester or publishing it. A handed-off candidate consumes its version even if it is never released: any changed binary needs another version. Do not replace a distributed installer with different bytes or move a published tag. Preserve old candidates for provenance and explicitly mark superseded ones. If a mistaken candidate number is withdrawn, record the correction and check updater ordering before handing off a lower-numbered replacement; automatic updates do not perform downgrades.

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
