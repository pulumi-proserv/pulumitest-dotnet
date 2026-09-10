# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-09-10

### Security

- `CleanupAsync()` no longer destroys a stack that existed before the run. Such a stack is selected, `StackPreexisted` is set, and destroy is skipped unless `OptTest.DestroyExistingStack()` is given
- Each program now uses a private local file backend by default; `OptTest.UseAmbientBackend()` opts into the `pulumi login` backend as the option always documented
- The temporary copy of the program excludes `.git`, `.env*`, `node_modules`, `bin`, `obj`, `__pycache__`, `.venv`, `venv`, and `.terraform`, skips symlinks that escape the program directory, is created readable only by the current user, and is deleted by `CleanupAsync()` (`OptTest.KeepTempDir()` keeps it)
- Documented that the default config passphrase is public and that `GetEnvVars()` returns secrets
- Bumped `Pulumi.Automation` to 3.113.2 and dropped the unsupported `net6.0` target framework
- Added `SECURITY.md` and Dependabot configuration

### Added

- `PulumiProgram` API for testing Pulumi programs using the Automation API, ported from [pulumitest-python](https://github.com/pulumi-labs/pulumitest-python)
- Framework-agnostic design: works with xUnit, NUnit, MSTest, or standalone programs
- Configuration options via `OptTest` (`TestInPlace`, `SkipInstall`, `StackName`, `ConfigPassphrase`, etc.)
- Result assertion methods: `HasNoChanges`, `HasNoDeletes`, `HasNoReplacements`
- Result types: `UpdateResult`, `PreviewResult`, `RefreshResult` with access to outputs, summaries, and change summaries
- `UpdateSource` for drift testing (swap program files while maintaining the same stack)
- `CopyToTempDirAsync` for isolated test copies
- `CleanupAsync(raiseOnError)` to optionally surface destroy failures, plus `IAsyncDisposable` support
- Apply `Env()` custom environment variables to the Automation API workspace and stack, so options like `PULUMI_BACKEND_URL` take effect
- Direct access to Pulumi Automation API via `CurrentStack` and `LocalWorkspace` properties
- CI pipeline with format check, build, and tests targeting .NET 8
- Tag-triggered release pipeline publishing a NuGet package to GitHub Releases

[Unreleased]: https://github.com/pulumi-proserv/pulumitest-dotnet/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/pulumi-proserv/pulumitest-dotnet/releases/tag/v0.1.0
