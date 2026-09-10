// Copyright 2026, Pulumi Corporation.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulumiTest.Tests;

/// <summary>
/// Test PulumiProgram construction and operations (no cloud credentials needed).
/// </summary>
/// <remarks>
/// The Automation API itself is replaced with <see cref="FakeBackend"/>, so
/// these tests exercise the guard logic (pre-existing stack handling, backend
/// isolation, temp directory hygiene, and copy filtering) without shelling
/// out to the real Pulumi CLI. Behaviour that only the real CLI would surface
/// (e.g. whether <c>pulumi stack init</c> actually raises
/// <c>StackAlreadyExistsException</c> for a stack that exists in a real
/// backend) is not covered here; <see cref="FakeWorkspace.StackAlreadyExists"/>
/// simulates that outcome instead.
/// </remarks>
public class PulumiProgramTests : IDisposable
{
    private readonly FakeBackend _backend = new();
    private readonly string _sandbox;
    private readonly string _source;
    private readonly string _tempDir;

    public PulumiProgramTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "pulumitest-" + Guid.NewGuid().ToString("N")[..8]);
        _source = Path.Combine(_sandbox, "my_program");
        _tempDir = Path.Combine(_sandbox, "tmp");
        Directory.CreateDirectory(Path.Combine(_source, "nested"));
        File.WriteAllText(Path.Combine(_source, "Pulumi.yaml"), "name: my_program\nruntime: dotnet\n");
        File.WriteAllText(Path.Combine(_source, "Program.cs"), "// v1\n");
        File.WriteAllText(Path.Combine(_source, "nested", "file.txt"), "nested\n");
    }

    public void Dispose()
    {
        Directory.Delete(_sandbox, recursive: true);
    }

    private Task<PulumiProgram> CreateAsync(params Option[] opts)
        => CreateAsync(NullLogger.Instance, opts);

    private Task<PulumiProgram> CreateAsync(ILogger logger, params Option[] opts)
        => PulumiProgram.CreateAsync(
            "test_stack",
            _backend,
            null,
            logger,
            default,
            new[] { OptTest.TempDir(_tempDir) }.Concat(opts).ToArray());

    private Task<PulumiProgram> CreateFromSourceAsync(params Option[] opts)
        => PulumiProgram.CreateAsync(
            _source,
            _backend,
            null,
            NullLogger.Instance,
            default,
            new[] { OptTest.TempDir(_tempDir) }.Concat(opts).ToArray());

    [Fact]
    public async Task Create_InitializesWithoutCopyingOrCreatingStack()
    {
        var program = await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall(), OptTest.SkipStackCreate());

        Assert.Equal("test_stack", program.WorkingDir);
        Assert.True(program.Options.TestInPlace);
        Assert.True(program.Options.SkipInstall);
        Assert.True(program.Options.SkipStackCreate);
        Assert.Null(program.CurrentStack);
        Assert.Single(_backend.WorkspaceCalls);
        Assert.Equal("test_stack", _backend.WorkspaceCalls[0].WorkDir);
        Assert.Equal(0, _backend.Workspace.InstallCalls);
        Assert.Empty(_backend.Workspace.StackNames);
    }

    [Fact]
    public async Task Create_CreatesStackWhenNotSkipped()
    {
        await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall());
        Assert.Equal(new[] { "test" }, _backend.Workspace.StackNames);
    }

    [Fact]
    public async Task Create_RunsInstallWhenNotSkipped()
    {
        await CreateAsync(OptTest.TestInPlace(), OptTest.SkipStackCreate());
        Assert.Equal(1, _backend.Workspace.InstallCalls);
    }

    [Fact]
    public async Task Create_UsesCustomStackName()
    {
        await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall(), OptTest.StackName("custom"));
        Assert.Equal(new[] { "custom" }, _backend.Workspace.StackNames);
    }

    [Fact]
    public async Task Create_ExposesEnvVars()
    {
        var program = await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall(), OptTest.SkipStackCreate());

        var envVars = program.GetEnvVars();
        Assert.Equal(OptTest.DefaultConfigPassphrase, envVars["PULUMI_CONFIG_PASSPHRASE"]);
        Assert.Contains("PULUMI_BACKEND_URL", envVars.Keys);
    }

    [Fact]
    public async Task Create_UsesCustomConfigPassphrase()
    {
        var program = await CreateAsync(
            OptTest.TestInPlace(),
            OptTest.SkipInstall(),
            OptTest.SkipStackCreate(),
            OptTest.ConfigPassphrase("hunter2"));
        Assert.Equal("hunter2", program.GetEnvVars()["PULUMI_CONFIG_PASSPHRASE"]);
    }

    [Fact]
    public async Task Create_PassesEnvVarsToWorkspace()
    {
        var program = await CreateAsync(
            OptTest.TestInPlace(),
            OptTest.SkipInstall(),
            OptTest.Env("PULUMI_BACKEND_URL", "file:///tmp/test-backend"),
            OptTest.Env("MY_CUSTOM_VAR", "hello"));

        var expected = new Dictionary<string, string>
        {
            ["PULUMI_BACKEND_URL"] = "file:///tmp/test-backend",
            ["PULUMI_CONFIG_PASSPHRASE"] = OptTest.DefaultConfigPassphrase,
            ["MY_CUSTOM_VAR"] = "hello",
        };
        Assert.Equal(expected, program.GetEnvVars());
        Assert.Equal(expected, _backend.WorkspaceCalls[0].Env);
    }

    [Fact]
    public async Task Create_DefaultBackend_IsPrivateLocalFileBackendUnderTempDir()
    {
        var program = await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall(), OptTest.SkipStackCreate());

        var backendUrl = program.GetEnvVars()["PULUMI_BACKEND_URL"];
        Assert.StartsWith("file://", backendUrl);
        var backendPath = new Uri(backendUrl).LocalPath;
        Assert.StartsWith(Path.GetFullPath(_tempDir), Path.GetFullPath(backendPath));
        Assert.True(Directory.Exists(backendPath));
    }

    [Fact]
    public async Task Create_DefaultBackend_LivesUnderOwnedTempDirWhenProgramWasCopied()
    {
        var program = await CreateFromSourceAsync(OptTest.SkipInstall(), OptTest.SkipStackCreate());

        var backendUrl = program.GetEnvVars()["PULUMI_BACKEND_URL"];
        var backendPath = new Uri(backendUrl).LocalPath;
        var programDir = Path.GetDirectoryName(program.WorkingDir)!;
        Assert.Equal(Path.Combine(programDir, "backend"), Path.GetFullPath(backendPath).TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public async Task Create_AmbientBackend_LeavesBackendUrlUnset()
    {
        var program = await CreateAsync(
            OptTest.TestInPlace(),
            OptTest.SkipInstall(),
            OptTest.SkipStackCreate(),
            OptTest.UseAmbientBackend());

        Assert.DoesNotContain("PULUMI_BACKEND_URL", program.GetEnvVars().Keys);
    }

    [Fact]
    public async Task Create_ExplicitBackendEnvVar_TakesPrecedenceOverPrivateBackend()
    {
        var program = await CreateAsync(
            OptTest.TestInPlace(),
            OptTest.SkipInstall(),
            OptTest.SkipStackCreate(),
            OptTest.Env("PULUMI_BACKEND_URL", "file:///explicit"));

        Assert.Equal("file:///explicit", program.GetEnvVars()["PULUMI_BACKEND_URL"]);
    }

    [Fact]
    public async Task Operations_ThrowWhenStackNotInitialized()
    {
        var program = await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall(), OptTest.SkipStackCreate());

        await Assert.ThrowsAsync<InvalidOperationException>(() => program.UpAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => program.PreviewAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => program.RefreshAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => program.DestroyAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => program.AddEnvironmentsAsync("a/b"));
    }

    [Fact]
    public async Task Operations_ForwardToStack()
    {
        var program = await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall());

        var up = await program.UpAsync();
        Assert.Equal("up output", up.StandardOutput);
        var preview = await program.PreviewAsync();
        Assert.Empty(preview.ChangeSummary);
        var refresh = await program.RefreshAsync();
        Assert.Null(refresh.ChangeSummary);
        await program.DestroyAsync();
        await program.AddEnvironmentsAsync("aws/dev", "shared/base");

        var stack = _backend.Workspace.Stack;
        Assert.Equal(1, stack.UpCalls);
        Assert.Equal(1, stack.PreviewCalls);
        Assert.Equal(1, stack.RefreshCalls);
        Assert.Equal(1, stack.DestroyCalls);
        Assert.Equal(0, stack.RemoveCalls);
        Assert.Equal(new[] { "aws/dev", "shared/base" }, stack.Environments);
    }

    [Fact]
    public async Task Cleanup_IsSafeWhenNoStackExists()
    {
        var program = await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall(), OptTest.SkipStackCreate());

        await program.CleanupAsync();
        await program.CleanupAsync(raiseOnError: true);

        Assert.Equal(0, _backend.Workspace.Stack.DestroyCalls);
    }

    [Fact]
    public async Task Cleanup_DestroysAndRemovesStack()
    {
        var program = await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall());

        await program.CleanupAsync();

        Assert.Equal(1, _backend.Workspace.Stack.DestroyCalls);
        Assert.Equal(1, _backend.Workspace.Stack.RemoveCalls);
    }

    [Fact]
    public async Task DisposeAsync_RunsCleanup()
    {
        var program = await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall());

        await program.DisposeAsync();

        Assert.Equal(1, _backend.Workspace.Stack.DestroyCalls);
        Assert.Equal(1, _backend.Workspace.Stack.RemoveCalls);
    }

    [Fact]
    public async Task Cleanup_SwallowsDestroyErrorByDefault()
    {
        var logger = new CapturingLogger();
        var program = await CreateAsync(logger, OptTest.TestInPlace(), OptTest.SkipInstall());
        _backend.Workspace.Stack.DestroyError = new InvalidOperationException("destroy failed: resource still in use");

        await program.CleanupAsync();

        Assert.Equal(1, _backend.Workspace.Stack.DestroyCalls);
        Assert.Equal(0, _backend.Workspace.Stack.RemoveCalls);
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("Destroy failed for stack 'test'", error.Message);
        Assert.Same(_backend.Workspace.Stack.DestroyError, error.Exception);
    }

    [Fact]
    public async Task Cleanup_RethrowsDestroyErrorWhenRequested()
    {
        var program = await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall());
        _backend.Workspace.Stack.DestroyError = new InvalidOperationException("destroy failed: resource still in use");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => program.CleanupAsync(raiseOnError: true));

        Assert.Contains("destroy failed", ex.Message);
        Assert.Equal(1, _backend.Workspace.Stack.DestroyCalls);
    }

    [Fact]
    public async Task Create_PreExistingStack_IsSelectedAndFlagged()
    {
        _backend.Workspace.StackAlreadyExists = true;
        var logger = new CapturingLogger();

        var program = await CreateAsync(logger, OptTest.TestInPlace(), OptTest.SkipInstall());

        Assert.True(program.StackPreexisted);
        Assert.Contains(logger.Entries, e => e.Message.Contains("already existed and was selected"));
    }

    [Fact]
    public async Task Cleanup_SkipsDestroyForPreExistingStackByDefault()
    {
        _backend.Workspace.StackAlreadyExists = true;
        var logger = new CapturingLogger();
        var program = await CreateAsync(logger, OptTest.TestInPlace(), OptTest.SkipInstall());

        await program.CleanupAsync();

        Assert.Equal(0, _backend.Workspace.Stack.DestroyCalls);
        Assert.Equal(0, _backend.Workspace.Stack.RemoveCalls);
        Assert.Contains(logger.Entries, e => e.Message.Contains("existed before this run"));
    }

    [Fact]
    public async Task Cleanup_DestroysPreExistingStackWhenOptedIn()
    {
        _backend.Workspace.StackAlreadyExists = true;
        var program = await CreateAsync(OptTest.TestInPlace(), OptTest.SkipInstall(), OptTest.DestroyExistingStack());

        await program.CleanupAsync();

        Assert.Equal(1, _backend.Workspace.Stack.DestroyCalls);
        Assert.Equal(1, _backend.Workspace.Stack.RemoveCalls);
    }

    [Fact]
    public async Task Create_CopiesProgramToTempDirByDefault()
    {
        var program = await CreateFromSourceAsync(OptTest.SkipInstall(), OptTest.SkipStackCreate());

        Assert.NotEqual(_source, program.WorkingDir);
        Assert.Equal("my_program", Path.GetFileName(program.WorkingDir));
        var programDir = Path.GetDirectoryName(program.WorkingDir)!;
        Assert.Equal(_tempDir, Path.GetDirectoryName(programDir));
        Assert.Matches("^programDir_[0-9a-f]{8}$", Path.GetFileName(programDir));
        Assert.Equal("// v1\n", File.ReadAllText(Path.Combine(program.WorkingDir, "Program.cs")));
        Assert.Equal("nested\n", File.ReadAllText(Path.Combine(program.WorkingDir, "nested", "file.txt")));
        Assert.Equal(program.WorkingDir, _backend.WorkspaceCalls[0].WorkDir);
    }

    [Fact]
    public async Task TempDirsAndBackendDir_AreCreatedPrivateOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var program = await CreateFromSourceAsync(OptTest.SkipInstall(), OptTest.SkipStackCreate());

        var programDir = Path.GetDirectoryName(program.WorkingDir)!;
        var backendPath = new Uri(program.GetEnvVars()["PULUMI_BACKEND_URL"]).LocalPath;

        foreach (var dir in new[] { programDir, program.WorkingDir, backendPath })
        {
            var mode = File.GetUnixFileMode(dir);
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                mode);
        }
    }

    [Fact]
    public async Task Copy_ExcludesSensitiveAndBuildDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_source, ".git"));
        File.WriteAllText(Path.Combine(_source, ".git", "config"), "secret\n");
        File.WriteAllText(Path.Combine(_source, ".env"), "SECRET=1\n");
        File.WriteAllText(Path.Combine(_source, ".env.local"), "SECRET=2\n");
        Directory.CreateDirectory(Path.Combine(_source, "node_modules"));
        File.WriteAllText(Path.Combine(_source, "node_modules", "pkg.js"), "// pkg\n");
        Directory.CreateDirectory(Path.Combine(_source, "bin"));
        Directory.CreateDirectory(Path.Combine(_source, "obj"));
        Directory.CreateDirectory(Path.Combine(_source, "__pycache__"));
        Directory.CreateDirectory(Path.Combine(_source, ".venv"));
        Directory.CreateDirectory(Path.Combine(_source, "venv"));
        Directory.CreateDirectory(Path.Combine(_source, ".terraform"));

        var program = await CreateFromSourceAsync(OptTest.SkipInstall(), OptTest.SkipStackCreate());

        foreach (var excluded in new[] { ".git", ".env", ".env.local", "node_modules", "bin", "obj", "__pycache__", ".venv", "venv", ".terraform" })
        {
            Assert.False(
                File.Exists(Path.Combine(program.WorkingDir, excluded)) || Directory.Exists(Path.Combine(program.WorkingDir, excluded)),
                $"expected '{excluded}' not to be copied");
        }

        Assert.True(File.Exists(Path.Combine(program.WorkingDir, "Program.cs")));
    }

    [Fact]
    public async Task Copy_SkipsSymlinksThatEscapeTheSourceRoot()
    {
        var outsideFile = Path.Combine(_sandbox, "outside.txt");
        File.WriteAllText(outsideFile, "outside\n");
        var escapingLink = Path.Combine(_source, "escaping-link.txt");
        File.CreateSymbolicLink(escapingLink, outsideFile);

        var program = await CreateFromSourceAsync(OptTest.SkipInstall(), OptTest.SkipStackCreate());

        Assert.False(File.Exists(Path.Combine(program.WorkingDir, "escaping-link.txt")));
    }

    [Fact]
    public async Task Copy_KeepsSymlinksThatStayInsideTheSourceRoot()
    {
        var insideLink = Path.Combine(_source, "inside-link.txt");
        File.CreateSymbolicLink(insideLink, Path.Combine(_source, "Program.cs"));

        var program = await CreateFromSourceAsync(OptTest.SkipInstall(), OptTest.SkipStackCreate());

        var copiedLink = Path.Combine(program.WorkingDir, "inside-link.txt");
        Assert.True(File.Exists(copiedLink));
        Assert.Equal("// v1\n", File.ReadAllText(copiedLink));
    }

    [Fact]
    public async Task Copy_SelfContainingTempDir_DoesNotRecurseIntoTempBase()
    {
        // The default temp base (./tmp under the program directory) sits
        // inside the source when testing from the project root; copying must
        // not recurse into it (or into the destination it creates).
        var selfTempDir = Path.Combine(_source, "tmp");
        Directory.CreateDirectory(selfTempDir);
        File.WriteAllText(Path.Combine(selfTempDir, "stale.txt"), "stale\n");

        var program = await PulumiProgram.CreateAsync(
            _source,
            _backend,
            null,
            NullLogger.Instance,
            default,
            OptTest.TempDir(selfTempDir),
            OptTest.SkipInstall(),
            OptTest.SkipStackCreate());

        Assert.False(Directory.Exists(Path.Combine(program.WorkingDir, "tmp")));
    }

    [Fact]
    public async Task Cleanup_RemovesTempDirsAfterSuccessfulDestroy()
    {
        var program = await CreateFromSourceAsync(OptTest.SkipInstall());
        var programDir = Path.GetDirectoryName(program.WorkingDir)!;
        var backendPath = new Uri(program.GetEnvVars()["PULUMI_BACKEND_URL"]).LocalPath;
        Assert.True(Directory.Exists(programDir));

        await program.CleanupAsync();

        Assert.False(Directory.Exists(programDir));
        Assert.False(Directory.Exists(backendPath));
    }

    [Fact]
    public async Task Cleanup_RemovesTempDirsWhenNoStackExists()
    {
        var program = await CreateFromSourceAsync(OptTest.SkipInstall(), OptTest.SkipStackCreate());
        var programDir = Path.GetDirectoryName(program.WorkingDir)!;

        await program.CleanupAsync();

        Assert.False(Directory.Exists(programDir));
    }

    [Fact]
    public async Task Cleanup_KeepsTempDirsWhenKeepTempDirIsSet()
    {
        var program = await CreateFromSourceAsync(OptTest.SkipInstall(), OptTest.KeepTempDir());
        var programDir = Path.GetDirectoryName(program.WorkingDir)!;

        await program.CleanupAsync();

        Assert.True(Directory.Exists(programDir));
    }

    [Fact]
    public async Task Cleanup_KeepsTempDirsWhenDestroyFails()
    {
        var program = await CreateFromSourceAsync(OptTest.SkipInstall());
        _backend.Workspace.Stack.DestroyError = new InvalidOperationException("destroy failed");
        var programDir = Path.GetDirectoryName(program.WorkingDir)!;

        await program.CleanupAsync();

        Assert.True(Directory.Exists(programDir));
    }

    [Fact]
    public async Task UpdateSource_ReplacesFilesButPreservesProjectAndState()
    {
        var program = await CreateFromSourceAsync(OptTest.SkipInstall(), OptTest.SkipStackCreate());
        Directory.CreateDirectory(Path.Combine(program.WorkingDir, ".pulumi"));
        File.WriteAllText(Path.Combine(program.WorkingDir, ".pulumi", "state"), "state\n");

        var modified = Path.Combine(_sandbox, "modified");
        Directory.CreateDirectory(Path.Combine(modified, ".pulumi"));
        File.WriteAllText(Path.Combine(modified, "Pulumi.yaml"), "name: SHOULD_NOT_COPY\n");
        File.WriteAllText(Path.Combine(modified, "Pulumi.test.yaml"), "config: {}\n");
        File.WriteAllText(Path.Combine(modified, ".pulumi", "state"), "SHOULD_NOT_COPY\n");
        File.WriteAllText(Path.Combine(modified, "Program.cs"), "// v2\n");
        File.WriteAllText(Path.Combine(modified, "Extra.cs"), "// extra\n");

        program.UpdateSource(modified);

        string Read(string relative) => File.ReadAllText(Path.Combine(program.WorkingDir, relative));
        Assert.Equal("// v2\n", Read("Program.cs"));
        Assert.Equal("// extra\n", Read("Extra.cs"));
        Assert.Equal("name: my_program\nruntime: dotnet\n", Read("Pulumi.yaml"));
        Assert.Equal("state\n", Read(Path.Combine(".pulumi", "state")));
        Assert.False(File.Exists(Path.Combine(program.WorkingDir, "Pulumi.test.yaml")));
    }

    [Fact]
    public async Task CopyTo_CreatesInPlaceProgramInTargetDirectory()
    {
        var program = await PulumiProgram.CreateAsync(
            _source, _backend, null, NullLogger.Instance, default,
            OptTest.TestInPlace(), OptTest.SkipInstall(), OptTest.SkipStackCreate());

        var target = Path.Combine(_sandbox, "copy");
        var copy = await program.CopyToAsync(target, OptTest.StackName("copied"));

        Assert.Equal(target, copy.WorkingDir);
        Assert.True(copy.Options.TestInPlace);
        Assert.True(copy.Options.SkipInstall);
        Assert.Equal("copied", copy.Options.StackName);
        Assert.Equal("test", program.Options.StackName);
        Assert.Same(program.Logger, copy.Logger);
        Assert.Equal("// v1\n", File.ReadAllText(Path.Combine(target, "Program.cs")));
    }

    [Fact]
    public async Task CopyToTempDir_CreatesProgramUnderTempDirectory()
    {
        var program = await PulumiProgram.CreateAsync(
            _source, _backend, null, NullLogger.Instance, default,
            OptTest.TestInPlace(), OptTest.SkipInstall(), OptTest.SkipStackCreate(), OptTest.TempDir(_tempDir));

        var copy = await program.CopyToTempDirAsync();

        Assert.True(copy.Options.TestInPlace);
        Assert.Equal(_tempDir, Path.GetDirectoryName(Path.GetDirectoryName(copy.WorkingDir)));
        Assert.Equal("// v1\n", File.ReadAllText(Path.Combine(copy.WorkingDir, "Program.cs")));
    }

    [Fact]
    public async Task CopyToTempDir_CopyOwnsItsDirectoryAndRemovesItOnCleanup()
    {
        var program = await PulumiProgram.CreateAsync(
            _source, _backend, null, NullLogger.Instance, default,
            OptTest.TestInPlace(), OptTest.SkipInstall(), OptTest.SkipStackCreate(), OptTest.TempDir(_tempDir));

        var copy = await program.CopyToTempDirAsync();
        var programDir = Path.GetDirectoryName(copy.WorkingDir)!;
        Assert.True(Directory.Exists(programDir));

        await copy.CleanupAsync();

        Assert.False(Directory.Exists(programDir));
        Assert.True(Directory.Exists(_source));
    }

    [Fact]
    public async Task CopyTo_CopyDoesNotRemoveCallerChosenDirectoryOnCleanup()
    {
        var program = await PulumiProgram.CreateAsync(
            _source, _backend, null, NullLogger.Instance, default,
            OptTest.TestInPlace(), OptTest.SkipInstall(), OptTest.SkipStackCreate(), OptTest.TempDir(_tempDir));

        var target = Path.Combine(Path.GetDirectoryName(_source)!, "copy");
        var copy = await program.CopyToAsync(target);
        await copy.CleanupAsync();

        Assert.True(File.Exists(Path.Combine(target, "Program.cs")));
    }
}
