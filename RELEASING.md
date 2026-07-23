# Releasing

This project uses [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt)
for release automation and [Conventional Commits](https://www.conventionalcommits.org/)
for versioning.

## Commit conventions

PR titles must follow the conventional commit format (enforced by CI):

| Prefix | Version bump | Example |
| --- | --- | --- |
| `feat:` | minor | `feat: add new middleware` |
| `fix:` | patch | `fix: correct routing behavior` |
| `feat!:` | major | `feat!: rename handler type` |
| `chore:` | patch | `chore: update dependencies` |
| `docs:` | patch | `docs: update README` |
| `refactor:` | patch | `refactor: simplify handler composition` |

Other valid prefixes: `test`, `perf`, `ci`, `build`, `style`, `revert`.

## Creating a release

Releases are automated. Every push to `main` runs the `shipit-pr` job in the
publish workflow, which:

1. Analyzes commits since the last release (tracked via `last_commit_released`
   in `CHANGELOG.md`)
2. Determines the next semantic version
3. Opens (or updates) a **release pull request** titled `chore: release X.Y.Z`
   with the updated `CHANGELOG.md`

Review and merge that PR to ship. Merging lands a `chore: release X.Y.Z` commit
on `main`, which triggers the `publish` job:

1. Packs the NuGet packages (`Fable.Giraffe.Python`, `Fable.Giraffe.Js`,
   `Fable.Giraffe.Beam`) at the release version
2. Pushes them to nuget.org using the `NUGET_API_KEY` secret
3. Creates the `vX.Y.Z` GitHub release

To preview or generate the release PR locally instead:

```bash
just shipit
```

## Prerequisites

- `NUGET_API_KEY` repository secret (glob pattern: `Fable.Giraffe*`)
- `GITHUB_TOKEN` or `gh` CLI authenticated (for ShipIt to create releases)
