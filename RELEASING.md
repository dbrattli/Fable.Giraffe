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

```bash
just shipit
```

This will:

1. Analyze commits since the last release
2. Determine the next semantic version
3. Update `CHANGELOG.md`
4. Create a GitHub release with the version tag (e.g. `v0.1.0`)

The GitHub release triggers the publish workflow, which:

1. Packs all NuGet packages (`Fable.Giraffe.Python`, etc.)
2. Pushes them to nuget.org using the `NUGET_API_KEY` secret

## Prerequisites

- `NUGET_API_KEY` repository secret (glob pattern: `Fable.Giraffe*`)
- `GITHUB_TOKEN` or `gh` CLI authenticated (for ShipIt to create releases)
