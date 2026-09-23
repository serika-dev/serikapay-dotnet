# Contributing

Thanks for helping improve the SerikaPay .NET library.

## Development

```bash
dotnet test
```

Tests run against a local mock server, so you don't need an API key or network access.

## Pull requests

- Open an issue first for anything bigger than a small fix, so we can agree on the approach.
- Keep the public API in line with the other SerikaPay libraries: same resource and method
  names, same retry and error behaviour. The reference is the
  [API docs](https://wallet.serika.dev/developers/docs).
- Add or update tests for every behaviour change, and a line under `Unreleased` in `CHANGELOG.md`.
- Make sure the test suite passes before you ask for review.

## Releasing (maintainers)

1. Bump the version in the package manifest and move `Unreleased` notes under the new version in `CHANGELOG.md`.
2. Commit, then tag: `git tag v1.2.3 && git push origin v1.2.3`.
3. The release workflow runs the tests, publishes to NuGet and creates the GitHub release.
