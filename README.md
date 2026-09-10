# Pulumitest .NET

A .NET port of [pulumitest](https://github.com/pulumi/pulumitest) (Go) for testing Pulumi programs using the Automation API. Framework-agnostic: works with xUnit, NUnit, MSTest, or standalone.

Sibling ports exist for [Python](https://github.com/pulumi-labs/pulumitest-python) and [TypeScript](https://github.com/pulumi-proserv/pulumitest-typescript) with the same API surface.

## Installation

```bash
dotnet add package PulumiTest
```

Or from a GitHub Release, download the `.nupkg` and add its directory as a package source:

```bash
dotnet nuget add source ./packages --name pulumitest-local
dotnet add package PulumiTest --version 0.1.0
```

## Quick Start

The Automation API is asynchronous, so a program is created with the `PulumiProgram.CreateAsync()` factory and every operation returns a `Task`. `PulumiProgram` implements `IAsyncDisposable`, so `await using` runs cleanup automatically.

### xUnit

```csharp
using PulumiTest;

public class StackTests : IAsyncLifetime
{
    private PulumiProgram _program = null!;

    public async Task InitializeAsync()
    {
        _program = await PulumiProgram.CreateAsync("my-pulumi-project");
        await _program.AddEnvironmentsAsync("my-org/aws-dev");
    }

    public Task DisposeAsync() => _program.CleanupAsync();

    [Fact]
    public async Task Deploys()
    {
        var result = await _program.UpAsync();
        Assert.Contains("bucketName", result.Outputs.Keys);

        var preview = await _program.PreviewAsync();
        preview.HasNoChanges();
    }
}
```

### NUnit

```csharp
using PulumiTest;

public class StackTests
{
    private PulumiProgram _program = null!;

    [OneTimeSetUp]
    public async Task SetUp() => _program = await PulumiProgram.CreateAsync("my-pulumi-project");

    [OneTimeTearDown]
    public Task TearDown() => _program.CleanupAsync();

    [Test]
    public async Task Deploys() => await _program.UpAsync();
}
```

### Standalone

```csharp
using PulumiTest;

await using var program = await PulumiProgram.CreateAsync("my-pulumi-project");
var result = await program.UpAsync();
Console.WriteLine($"Outputs: {string.Join(", ", result.Outputs.Keys)}");
```

## Configuration Options

```csharp
var program = await PulumiProgram.CreateAsync(
    "my-pulumi-project",
    OptTest.TestInPlace(),          // Don't copy to temp directory
    OptTest.SkipInstall(),          // Skip pulumi install
    OptTest.StackName("dev"),       // Custom stack name
    OptTest.ConfigPassphrase("x")); // Set config passphrase
```

| Option                    | Description                                                       |
| ------------------------- | ------------------------------------------------------------------ |
| `TestInPlace()`           | Run from source directory (no copy)                                |
| `SkipInstall()`           | Skip `pulumi install`                                              |
| `SkipStackCreate()`       | Skip stack creation (must exist)                                   |
| `StackName(name)`         | Set custom stack name                                              |
| `ConfigPassphrase(p)`     | Set config passphrase                                              |
| `TempDir(path)`           | Set custom temp directory                                          |
| `UseAmbientBackend()`     | Use existing `pulumi login` backend instead of a private one       |
| `Env(key, value)`         | Set custom environment variable                                    |
| `DestroyExistingStack()`  | Let `CleanupAsync()` destroy a stack that existed before the run   |
| `KeepTempDir()`           | Leave the temporary copy on disk after `CleanupAsync()`             |

The temp directory defaults to `./tmp` under the current working directory, or `$PULUMITEST_TEMP_DIR` when set. Temp directories are created readable only by the current user and are deleted by `CleanupAsync()`.

### Isolation defaults

- **Backend.** Each program gets a private local file backend under its temp directory, so test stacks never reach the backend `pulumi login` points at. Pass `UseAmbientBackend()` when a test needs Pulumi Cloud, for example to attach ESC environments, or set `Env("PULUMI_BACKEND_URL", ...)` explicitly.
- **Pre-existing stacks.** If the stack name already exists, it is selected rather than created and `program.StackPreexisted` is `true`. `CleanupAsync()` will not destroy it unless `DestroyExistingStack()` was given. This matters with `TestInPlace()`, where the default stack name `test` may collide with a real stack in the project directory.
- **Copied files.** `.git`, `.env` and `.env.*`, `node_modules`, `bin`, `obj`, `__pycache__`, `.venv`, `venv`, and `.terraform` are never copied, and symlinks that point outside the program directory are skipped.
- **Passphrase.** The default config passphrase is the fixed, publicly known string `correct horse battery staple` (`OptTest.DefaultConfigPassphrase`). Secrets in a test stack's config are not protected by it. Pass `ConfigPassphrase()` with a real value if that matters.
- **`GetEnvVars()`** returns the passphrase and anything passed via `Env()`. Do not log it.

Environment variables from `Env()` are passed to the Automation API workspace and take precedence over the defaults, so `Env("PULUMI_BACKEND_URL", "file:///tmp/backend")` runs the stack against a local file backend instead of the private one created by default.

A custom `Microsoft.Extensions.Logging.ILogger` or pre-built `Options` can be supplied through the named-argument overload:

```csharp
var program = await PulumiProgram.CreateAsync(
    "my-pulumi-project",
    logger: loggerFactory.CreateLogger("pulumi"),
    opts: new[] { OptTest.SkipInstall() });
```

## Result Assertions

```csharp
var result = await program.UpAsync();
result.HasNoChanges();
result.HasNoDeletes();
result.HasNoReplacements();

var preview = await program.PreviewAsync();
preview.HasNoChanges();

var refresh = await program.RefreshAsync();
refresh.HasNoChanges();
```

All assertion methods throw `PulumiTestAssertionException` on failure, which every test framework reports as a test failure.

## Result Properties

```csharp
// UpdateResult
var result = await program.UpAsync();
result.Outputs;        // IImmutableDictionary<string, OutputValue>
result.Summary;        // UpdateSummary
result.ChangeSummary;  // IReadOnlyDictionary<OperationType, int>
result.RawResult;      // Pulumi.Automation.UpResult

// PreviewResult
var preview = await program.PreviewAsync();
preview.ChangeSummary; // IReadOnlyDictionary<OperationType, int>

// RefreshResult
var refresh = await program.RefreshAsync();
refresh.Summary;       // UpdateSummary
refresh.ChangeSummary; // IReadOnlyDictionary<OperationType, int>
```

## Cleanup

`CleanupAsync()` destroys the stack, removes it, and deletes the temporary copy of the program. A stack that existed before the run is left in place unless `DestroyExistingStack()` was given. The temporary directory is kept when the destroy fails so state can be inspected. By default a failed destroy is logged but not thrown, so teardown never masks the test result. Pass `raiseOnError: true` to make a failed destroy fail the teardown instead, which is useful in suites that must not leak cloud resources:

```csharp
public Task DisposeAsync() => _program.CleanupAsync(raiseOnError: true);
```

## Advanced Usage

### Update Source (Drift Testing)

Swap program files while maintaining the same stack. Top-level `Pulumi.yaml`, `Pulumi.test.yaml` and `.pulumi` entries are preserved:

```csharp
await using var program = await PulumiProgram.CreateAsync("example");

await program.UpAsync();
program.UpdateSource("path/to/modified");
var preview = await program.PreviewAsync();
// preview will show changes
```

### Copy to Temp Directory

```csharp
var program = await PulumiProgram.CreateAsync("example", OptTest.TestInPlace());
await using var copy = await program.CopyToTempDirAsync();
await copy.UpAsync();
```

### Access Pulumi Automation API

```csharp
var program = await PulumiProgram.CreateAsync("example");
var stack = program.CurrentStack;         // Pulumi.Automation.WorkspaceStack
var workspace = program.LocalWorkspace;   // Pulumi.Automation.LocalWorkspace
```

## Development

Prerequisites: .NET 8 SDK, [Pulumi CLI](https://www.pulumi.com/docs/install/), [just](https://github.com/casey/just).

```bash
git clone https://github.com/pulumi-proserv/pulumitest-dotnet.git
cd pulumitest-dotnet
just install # dotnet restore
just test    # run tests
just lint    # dotnet format --verify-no-changes
just pack    # build the NuGet package into dist/
```

## License

Apache 2.0
