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

## Public versions and internal testing

Choose a public version from the last **published** release and the complete accepted change set, using the table above. Test iterations do not count as public PATCH releases. Record an owner-selected target explicitly; do not silently change it because more bugs were fixed during testing.

Use ordinary numeric versions only. Do not introduce `rc`, prerelease channels, a second assembly-version scheme or a build counter for this process. `Directory.Build.props` remains the single version source for each actual build.

Internal tester versions may advance when useful for identifying installed builds, but a bump is not required for every fix or handoff. Those internal numbers do not reserve future public version numbers. Multiple unpublished builds with the same version must use separate output directories and retain their original installer, build log and existing commit/hash sidecar. Identify the exact tested artifact by path and commit, not version alone. Do not silently overwrite an artifact handed to a tester. Keep the current candidate and its provenance; obsolete unpublished installers, exports and reports may be deleted at the owner's request. There is no requirement to archive every tester iteration. Once a version is publicly released, its installer/catalog and tag are immutable; changed shipped code requires a new public version.

Keep one working branch through the entire preparation cycle. Do not create or rename a branch just because a tester version changed. Its name is a work label, not a version authority. `master` is the permanent branch; use a temporary stabilization branch when the owner requests one or parallel work requires it. Normal publication follows the separately approved merge into master, then the final build and tag there. The tag helper also supports an exact `release/<public-version>` branch for deliberate parallel maintenance; that exception does not require renaming the current tester branch.

### Agent decision sequence

1. Establish the last published release, owner-approved public target, current source version and last installed tester version. Do not infer publication from a branch, local tag or artifact folder alone. If public state is uncertain, verify it before changing the public target.
2. Continue fixes in the current working branch. Keep internal version numbers distinct from the public release decision. Documentation/test-only changes do not require another installer.
3. For a requested tester build, choose the internal numeric version as needed, commit the source and use a fresh output directory. Record version, commit, hash and acceptance status. Replace obsolete tester deliverables through explicit cleanup when requested.
4. Before public release, set `Directory.Build.props` to the agreed public target; prepare one set of public release notes covering changes since the last published release. Consolidate unpublished tester notes into the upcoming public release; remove obsolete standalone notes.
5. Perform the approved merge, commit the final versioned source and build the exact final installer. Revalidate version, payload, signing/provenance, normal upgrade and acceptance. Never relabel or rename a higher-version executable as a lower release. If merge/source changes after a candidate was built, rebuild from the final commit.
6. Publish only after explicit owner approval for the exact final artifact. Tag, catalog, notes and executable versions must agree.

### Higher-version testers moving to the public release

The updater offers only newer versions. Setup also refuses a lower version while a higher one is registered. Therefore a tester running 1.1.5 cannot install 1.1.0 over it or receive 1.1.0 automatically.

For the current cycle, the intended public release is **1.1.0** over the public 1.0.0 baseline; 1.1.1–1.1.5 are unpublished tester history. The current working branch is denis/release-1.1.0 and builds now use 1.1.0 for owner testing. This is an explicit owner-selected target, not a rule that every future batch warrants MINOR.

Before handing off the final build, verify in a disposable profile: preserve/back up the tester's settings, history and captures, uninstall the higher tester version normally, install the public version, and confirm retained data and normal operation. The uninstaller is designed to retain user data, but this exact transition still needs acceptance. Do not edit installed-version registry values, delete the user's profile or weaken downgrade protection. Host uninstall/install requires the user's authorization. Also test the ordinary 1.0.0 -> 1.1.0 upgrade separately. If data compatibility fails, stop and resolve that transition before release.

## Candidate checks

1. Review the source, public documentation, screenshots, dependency notices and license. Public screenshots must show the actual application with safe demonstration content.
2. Run locked restore, a full Release build and tests, public-tree validation, release-tool tests and Windows CI. Resolve failures before packaging.
3. Exercise screenshot → editor → save/copy/drag, and recording → pause/audio toggles → stop → edit/export on Windows. Record the tested displays, scaling and audio devices. Automated rendering does not replace physical input or listening.
4. In a disposable Windows profile, verify installation, update, repair, uninstall/reinstall, retained settings/history/media and startup preferences.
5. Verify the signed GitHub update path with an older installed candidate and a newer release: check, download, install, restart, retained data and offline cached installation. Confirm cancellation and rejection of damaged packages.
6. Confirm recoverable custody of the update signing key, the actual Authenticode signing status and a working private security-reporting channel. Catalog signing does not establish a Windows publisher.
7. Finalize release notes with tested behavior, known limitations and accurate installation/update instructions. Keep preparation notices until the release is actually available.

## Build and stage

Start from a clean, committed candidate. Replace `<version>` with the final source version, `<candidate-id>` with a fresh directory name (for example date and short commit), and `<build-id>` with the payload staging ID printed by the builder. These directory names are not extra product versions.

```powershell
dotnet restore SnappySnap.sln --locked-mode
./build.ps1 -Configuration Release
./test.ps1 -Configuration Release -NoBuild
./tools/release/Test-PublicTree.ps1 -RequireLicense
./build-installer.ps1 -OutputDirectory ./artifacts/installer/<candidate-id>
```

Retain the build log and `.exe.build.json` sidecar locally. Use the fresh staging path printed by the build when signing the catalog:

```powershell
./installer/publish-release.ps1 `
  -InstallerPath ./artifacts/installer/<candidate-id>/SnappySnap-Setup-<version>-x64.exe `
  -PublishedAppPath ./artifacts/installer-staging/<build-id>/SnappySnap.exe `
  -NotesPath ./docs/public/releases/<version>.md `
  -Destination ./artifacts/signed/<version>

./tools/release/Stage-Release.ps1 `
  -InstallerPath ./artifacts/installer/<candidate-id>/SnappySnap-Setup-<version>-x64.exe `
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
