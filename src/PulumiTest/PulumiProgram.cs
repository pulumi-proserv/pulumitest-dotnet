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
using Pulumi.Automation;

namespace PulumiTest;

/// <summary>
/// Framework-independent Pulumi program wrapper.
/// </summary>
/// <remarks>
/// <para>
/// Wraps Pulumi Automation API operations without any test framework
/// dependencies. Works with xUnit, NUnit, MSTest, or standalone. Provides a
/// <see cref="CleanupAsync"/> method for registration with any test
/// framework's teardown, and implements <see cref="IAsyncDisposable"/> so it
/// can be used with <c>await using</c>.
/// </para>
/// <para>
/// The Automation API is asynchronous, so instances are created with the
/// <see cref="CreateAsync(string, Option[])"/> factory rather than a constructor.
/// </para>
/// <example>
/// <code>
/// await using var program = await PulumiProgram.CreateAsync("test_stack");
/// var result = await program.UpAsync();
/// Assert.Contains("bucketName", result.Outputs.Keys);
/// </code>
/// </example>
/// </remarks>
public sealed class PulumiProgram : IAsyncDisposable
{
    /// <summary>The stack name used when none is configured.</summary>
    public const string DefaultStackName = "test";

    private static readonly HashSet<string> PreservedPaths = new(StringComparer.Ordinal)
    {
        ".pulumi",
        "Pulumi.yaml",
        "Pulumi.test.yaml",
    };

    /// <summary>
    /// Directory and file names never copied from the program under test.
    /// </summary>
    /// <remarks>
    /// These hold credentials (<c>.env*</c>), history that may contain old
    /// secrets (<c>.git</c>), or build output that is large and reproducible.
    /// </remarks>
    public static readonly IReadOnlySet<string> ExcludedNames = new HashSet<string>(StringComparer.Ordinal)
    {
        ".git",
        ".env",
        "node_modules",
        "bin",
        "obj",
        "__pycache__",
        ".venv",
        "venv",
        ".terraform",
    };

    private static int _loggerCounter;

    private readonly IAutomationBackend _backend;
    private readonly Dictionary<string, string> _envVars;
    private IWorkspaceHandle? _workspace;
    private IStackHandle? _stack;
    private string? _ownedTempDir;
    private string? _backendDir;

    /// <summary>The directory the program runs from. A temp copy unless <see cref="OptTest.TestInPlace"/> is set.</summary>
    public string WorkingDir { get; private set; }

    /// <summary>The effective options.</summary>
    public Options Options { get; }

    /// <summary>The logger in use.</summary>
    public ILogger Logger { get; }

    /// <summary>The current stack, or null when stack creation was skipped.</summary>
    public WorkspaceStack? CurrentStack => _stack?.Stack;

    /// <summary>The local workspace, or null before initialization.</summary>
    public LocalWorkspace? LocalWorkspace => _workspace?.Workspace;

    /// <summary>
    /// True when the stack already existed and was selected rather than
    /// created. <see cref="CleanupAsync"/> refuses to destroy such a stack
    /// unless <see cref="OptTest.DestroyExistingStack"/> was given.
    /// </summary>
    public bool StackPreexisted { get; private set; }

    private PulumiProgram(string workingDir, Options options, ILogger logger, IAutomationBackend backend)
    {
        WorkingDir = workingDir;
        Options = options;
        Logger = logger;
        _backend = backend;

        _envVars = new Dictionary<string, string>
        {
            ["PULUMI_CONFIG_PASSPHRASE"] = string.IsNullOrEmpty(options.ConfigPassphrase)
                ? OptTest.DefaultConfigPassphrase
                : options.ConfigPassphrase,
        };
    }

    /// <summary>
    /// Create a PulumiProgram for the program in <paramref name="workingDir"/>.
    /// </summary>
    /// <remarks>
    /// Unless <see cref="OptTest.TestInPlace"/> is given, the program is copied
    /// to a fresh temporary directory first. Then a local workspace is created,
    /// <c>pulumi install</c> is run (unless skipped) and the stack is created
    /// or selected (unless skipped).
    /// </remarks>
    public static Task<PulumiProgram> CreateAsync(string workingDir, params Option[] opts)
        => CreateAsync(workingDir, options: null, logger: null, cancellationToken: default, opts);

    /// <summary>
    /// Create a PulumiProgram with pre-built options and/or a custom logger.
    /// </summary>
    /// <param name="workingDir">Directory containing the Pulumi program.</param>
    /// <param name="options">Pre-built options. When given, <paramref name="opts"/> are applied on top of it.</param>
    /// <param name="logger">Logger to use. Defaults to a console logger.</param>
    /// <param name="cancellationToken">Cancellation token for the initialization steps.</param>
    /// <param name="opts">Options to apply.</param>
    public static Task<PulumiProgram> CreateAsync(
        string workingDir,
        Options? options = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default,
        params Option[] opts)
        => CreateAsync(workingDir, LocalAutomationBackend.Instance, options, logger, cancellationToken, opts);

    internal static async Task<PulumiProgram> CreateAsync(
        string workingDir,
        IAutomationBackend backend,
        Options? options,
        ILogger? logger,
        CancellationToken cancellationToken,
        params Option[] opts)
    {
        var effective = (options ?? OptTest.DefaultOptions()).Apply(opts);
        var program = new PulumiProgram(workingDir, effective, logger ?? CreateDefaultLogger(), backend);

        if (!program.Options.TestInPlace)
        {
            var (programDir, destination) = program.CreateTempDir();
            program._ownedTempDir = programDir;
            program.CopyToInternal(destination);
            program.WorkingDir = destination;
        }

        program.ConfigureBackend();
        await program.InitStackAsync(cancellationToken).ConfigureAwait(false);
        return program;
    }

    private static ILogger CreateDefaultLogger()
        => new ConsoleLogger($"PulumiProgram-{Interlocked.Increment(ref _loggerCounter)}");

    private static string ShortId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Create a directory readable only by the current user. On Windows,
    /// where POSIX permission bits do not apply, the default ACL is used.
    /// </summary>
    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static bool IsExcludedName(string name)
        => ExcludedNames.Contains(name) || name.StartsWith(".env.", StringComparison.Ordinal);

    private string TempBase()
        => string.IsNullOrEmpty(Options.TempDir)
            ? Path.Combine(Directory.GetCurrentDirectory(), "tmp")
            : Options.TempDir;

    /// <summary>
    /// Create <c>&lt;tempBase&gt;/programDir_&lt;id&gt;/&lt;program name&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Directories are private to the current user because the copy may
    /// include stack config and state.
    /// </remarks>
    private (string ProgramDir, string Destination) CreateTempDir()
    {
        var baseDir = TempBase();
        CreatePrivateDirectory(baseDir);

        var programDir = Path.Combine(baseDir, "programDir_" + ShortId());
        Logger.LogInformation("Creating temp directory {TempDir}", Path.GetFileName(programDir));
        // Created one level at a time: when a multi-level path is created in a
        // single call, only the final (leaf) directory gets the requested
        // mode and intermediate directories fall back to the default mode.
        CreatePrivateDirectory(programDir);

        var sourceBase = Path.GetFileName(Path.GetFullPath(WorkingDir).TrimEnd(Path.DirectorySeparatorChar));
        var destination = Path.Combine(programDir, sourceBase);
        CreatePrivateDirectory(destination);
        return (programDir, destination);
    }

    /// <summary>
    /// Pick the backend. Precedence: an explicit <c>Env("PULUMI_BACKEND_URL", ...)</c>,
    /// then the ambient backend if <see cref="OptTest.UseAmbientBackend"/> was
    /// given (the CLI inherits the process environment on its own), otherwise
    /// a private local file backend so test stacks never reach a shared backend.
    /// </summary>
    private void ConfigureBackend()
    {
        if (!Options.CustomEnv.ContainsKey("PULUMI_BACKEND_URL") && !Options.UseAmbientBackend)
        {
            string backendDir;
            if (_ownedTempDir is not null)
            {
                backendDir = Path.Combine(_ownedTempDir, "backend");
            }
            else
            {
                var baseDir = TempBase();
                CreatePrivateDirectory(baseDir);
                backendDir = Path.Combine(baseDir, "backend_" + ShortId());
            }

            CreatePrivateDirectory(backendDir);
            _backendDir = backendDir;
            _envVars["PULUMI_BACKEND_URL"] = new Uri(backendDir).AbsoluteUri;
        }

        // Custom env vars from the Env() option take precedence over defaults.
        foreach (var kv in Options.CustomEnv)
        {
            _envVars[kv.Key] = kv.Value;
        }
    }

    private void CopyToInternal(string directory)
    {
        try
        {
            CopyDirectory(WorkingDir, directory, WorkingDir, new[] { directory, TempBase() }, preserveTopLevel: null);
        }
        catch (IOException e)
        {
            throw new InvalidOperationException($"Error copying program to {directory}: {e.Message}", e);
        }
    }

    private async Task InitStackAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Creating local workspace...");
        _workspace = await _backend.CreateWorkspaceAsync(WorkingDir, _envVars, cancellationToken)
            .ConfigureAwait(false);

        if (!Options.SkipInstall)
        {
            Logger.LogInformation("Running pulumi install...");
            await _workspace.InstallAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!Options.SkipStackCreate)
        {
            var stackName = string.IsNullOrEmpty(Options.StackName) ? DefaultStackName : Options.StackName;
            Logger.LogInformation("Running pulumi stack init... (stack: {StackName})", stackName);
            var (stack, preExisted) = await _workspace.CreateOrSelectStackAsync(stackName, cancellationToken)
                .ConfigureAwait(false);
            _stack = stack;
            StackPreexisted = preExisted;
            if (preExisted)
            {
                Logger.LogInformation(
                    "Stack '{StackName}' already existed and was selected, not created. {Action}",
                    stackName,
                    Options.DestroyExistingStack
                        ? "CleanupAsync() will destroy it because DestroyExistingStack() was given."
                        : "CleanupAsync() will leave it in place; pass OptTest.DestroyExistingStack() to destroy it.");
            }
        }
        else
        {
            Logger.LogInformation("Skipping stack creation (SkipStackCreate=true)");
        }
    }

    /// <summary>
    /// Destroy and remove the stack, then delete the temporary copy of the
    /// program. Register with your test framework's teardown hook.
    /// </summary>
    /// <remarks>
    /// A stack that existed before this run is left untouched unless
    /// <see cref="OptTest.DestroyExistingStack"/> was given. The temporary
    /// directory is kept when the destroy fails, so the state is available
    /// for inspection, or when <see cref="OptTest.KeepTempDir"/> was given.
    /// </remarks>
    /// <param name="raiseOnError">
    /// Re-throw if the destroy fails. Defaults to false to preserve existing
    /// behaviour, but a failed destroy leaves real cloud resources behind, so
    /// suites that care about leaks should pass true and let the teardown fail loudly.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CleanupAsync(bool raiseOnError = false, CancellationToken cancellationToken = default)
    {
        if (_stack is null)
        {
            Logger.LogInformation("No current stack, skipping destroy...");
            RemoveTempDirs();
            return;
        }

        if (StackPreexisted && !Options.DestroyExistingStack)
        {
            Logger.LogInformation(
                "Stack '{StackName}' existed before this run; leaving it in place. Pass OptTest.DestroyExistingStack() to destroy it.",
                _stack.Name);
            return;
        }

        Logger.LogInformation("Running pulumi destroy and removing stack...");
        try
        {
            await _stack.DestroyAsync(cancellationToken).ConfigureAwait(false);
            await _stack.RemoveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LogError(
                e,
                "Destroy failed for stack '{StackName}'; cloud resources may have been left behind",
                _stack.Name);
            if (raiseOnError)
            {
                throw;
            }

            return;
        }

        RemoveTempDirs();
    }

    private void RemoveTempDirs()
    {
        if (Options.KeepTempDir)
        {
            return;
        }

        foreach (var dir in new[] { _ownedTempDir, _backendDir })
        {
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>Equivalent to <see cref="CleanupAsync"/> with default arguments.</summary>
    public ValueTask DisposeAsync() => new(CleanupAsync());

    /// <summary>Run <c>pulumi up</c>.</summary>
    public Task<UpdateResult> UpAsync(CancellationToken cancellationToken = default)
    {
        var stack = RequireStack();
        Logger.LogInformation("Running pulumi up on stack: {StackName}", stack.Name);
        return stack.UpAsync(cancellationToken);
    }

    /// <summary>Run <c>pulumi preview</c>.</summary>
    public Task<PreviewResult> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var stack = RequireStack();
        Logger.LogInformation("Running pulumi preview on stack: {StackName}", stack.Name);
        return stack.PreviewAsync(cancellationToken);
    }

    /// <summary>Run <c>pulumi refresh</c>.</summary>
    public Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var stack = RequireStack();
        Logger.LogInformation("Running pulumi refresh on stack: {StackName}", stack.Name);
        return stack.RefreshAsync(cancellationToken);
    }

    /// <summary>Run <c>pulumi destroy</c> without removing the stack.</summary>
    public Task DestroyAsync(CancellationToken cancellationToken = default)
    {
        var stack = RequireStack();
        Logger.LogInformation("Running pulumi destroy on stack: {StackName}", stack.Name);
        return stack.DestroyAsync(cancellationToken);
    }

    /// <summary>
    /// Update the working directory from <paramref name="sourceDir"/>, preserving stack state.
    /// </summary>
    /// <remarks>
    /// Top-level <c>.pulumi</c>, <c>Pulumi.yaml</c> and <c>Pulumi.test.yaml</c>
    /// entries in the source are not copied so the existing project and stack
    /// remain intact.
    /// </remarks>
    public void UpdateSource(string sourceDir)
    {
        Logger.LogInformation("Updating source from {SourceDir} to {WorkingDir}", sourceDir, WorkingDir);
        var sourceRoot = Path.GetFullPath(sourceDir);
        try
        {
            CopyDirectory(sourceRoot, WorkingDir, sourceRoot, new[] { WorkingDir }, preserveTopLevel: PreservedPaths);
        }
        catch (IOException e)
        {
            throw new InvalidOperationException($"Error updating source from {sourceDir}: {e.Message}", e);
        }
    }

    /// <summary>Add ESC environments to the stack.</summary>
    public Task AddEnvironmentsAsync(params string[] environmentNames)
        => AddEnvironmentsAsync(environmentNames, CancellationToken.None);

    /// <summary>Add ESC environments to the stack.</summary>
    public Task AddEnvironmentsAsync(IEnumerable<string> environmentNames, CancellationToken cancellationToken)
        => RequireStack().AddEnvironmentsAsync(environmentNames, cancellationToken);

    /// <summary>
    /// Get the environment variables for this workspace.
    /// </summary>
    /// <remarks>
    /// Includes the config passphrase and anything passed via
    /// <see cref="OptTest.Env"/>, which may be credentials. Do not log the
    /// returned dictionary.
    /// </remarks>
    public IReadOnlyDictionary<string, string> GetEnvVars() => new Dictionary<string, string>(_envVars);

    /// <summary>Copy the program to a new temporary directory.</summary>
    public Task<PulumiProgram> CopyToTempDirAsync(params Option[] opts)
    {
        var (_, destination) = CreateTempDir();
        return CopyToAsync(destination, opts);
    }

    /// <summary>Copy the program to the specified directory.</summary>
    public Task<PulumiProgram> CopyToAsync(string directory, params Option[] opts)
    {
        CopyToInternal(directory);
        var options = Options.Copy().Apply(opts);
        OptTest.TestInPlace()(options);
        return CreateAsync(directory, _backend, options, Logger, CancellationToken.None);
    }

    private IStackHandle RequireStack()
        => _stack ?? throw new InvalidOperationException("Stack not initialized");

    /// <summary>
    /// Recursive copy that skips <see cref="ExcludedNames"/>, refuses symlinks
    /// whose resolved target lies outside <paramref name="sourceRoot"/>, and
    /// never descends into any directory in <paramref name="avoid"/>
    /// (typically the destination and the temp base, so copying a program
    /// into its own subtree, e.g. the default <c>./tmp</c> under the source
    /// directory, cannot recurse into itself).
    /// </summary>
    private static void CopyDirectory(
        string sourceDir,
        string destDir,
        string sourceRoot,
        IReadOnlyList<string> avoid,
        HashSet<string>? preserveTopLevel)
    {
        var source = new DirectoryInfo(sourceDir);
        if (!source.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        CreatePrivateDirectory(destDir);

        foreach (var entry in source.EnumerateFileSystemInfos())
        {
            if (IsExcludedName(entry.Name))
            {
                continue;
            }

            if (preserveTopLevel is not null && preserveTopLevel.Contains(entry.Name))
            {
                continue;
            }

            var resolved = entry.FullName;
            if (avoid.Any(a => IsSameOrUnder(resolved, a)))
            {
                continue;
            }

            var target = Path.Combine(destDir, entry.Name);
            var linkTarget = entry.LinkTarget;
            var isReparsePoint = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

            if (linkTarget is not null)
            {
                CopySymlinkIfSafe(entry, linkTarget, target, sourceRoot);
            }
            else if (isReparsePoint)
            {
                // A reparse point we can't resolve a target for; skip it rather
                // than risk following it somewhere unexpected.
            }
            else if (entry is DirectoryInfo dir)
            {
                CopyDirectory(dir.FullName, target, sourceRoot, avoid, preserveTopLevel: null);
            }
            else
            {
                File.Copy(entry.FullName, target, overwrite: true);
            }
        }
    }

    private static void CopySymlinkIfSafe(FileSystemInfo entry, string linkTarget, string target, string sourceRoot)
    {
        var baseDir = Path.GetDirectoryName(entry.FullName)!;
        var resolvedTarget = Path.GetFullPath(linkTarget, baseDir);
        var normalizedRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!IsSameOrUnder(resolvedTarget, normalizedRoot))
        {
            // Symlink target escapes the program directory; skip it.
            return;
        }

        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
        else if (File.Exists(target))
        {
            File.Delete(target);
        }

        File.CreateSymbolicLink(target, linkTarget);
    }

    private static bool IsSameOrUnder(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
