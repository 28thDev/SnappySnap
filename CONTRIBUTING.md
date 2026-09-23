# Contributing

SnappySnap targets Windows 11 x64. Please keep changes focused on a concrete user problem and discuss larger feature proposals before implementing them.

1. Branch from `master`; use a short feature/fix branch name.
2. Follow the existing [architecture](docs/public/ARCHITECTURE.md), preserving capture state and native-library boundaries.
3. Run the [build and tests](docs/public/BUILDING.md); state which real Windows scenarios you exercised.
4. Update all supported locale catalogs together for user-facing changes. Less common diagnostic text may intentionally use the English fallback.
5. Open a pull request into `master`. After review and passing checks, squash-merge the change and delete its temporary branch.

`master` is the only permanent development branch. The maintainer may commit small, reviewed and locally verified fixes directly to it. Ordinary releases use a version tag on a verified commit; a temporary release branch is needed only for stabilization alongside newer development. See the [release procedure](docs/public/RELEASE_PROCESS.md).

Use the central version in `Directory.Build.props` and the [version decision table](docs/public/RELEASE_PROCESS.md#versions-and-source). Compatible improvements to an existing workflow, including small shortcuts or controls, use PATCH. A new independent product workflow or substantial capability uses MINOR; an approved incompatible change uses MAJOR. Documentation/build-only changes without shipped behavior need no bump. Public increments are measured from the last published release, not from internal tester builds. Keep one working branch throughout testing; do not rename it for each build. Internal numeric versions may advance without reserving public numbers. Build Setup only after source is committed, keep the current tester artifact in a fresh directory with commit/hash provenance; obsolete unpublished artifacts may be deleted when requested, and never overwrite a published version. See the release procedure for higher-version testers moving to a lower public version.

Do not include personal captures, account details, logs, private keys, build output or generated test evidence. Use synthetic fixtures. Keep dependencies pinned and document their purpose and license. Do not silently add telemetry, cloud services or bundled capture/codec applications.

For bug reports, include the application/Windows versions, reproduction steps, expected behavior and sanitized evidence. Security problems follow [SECURITY.md](SECURITY.md).
