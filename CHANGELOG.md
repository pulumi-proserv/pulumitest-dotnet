# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- CI pipeline with format check, build, and tests targeting .NET 6 and .NET 8
- Tag-triggered release pipeline publishing a NuGet package to GitHub Releases
