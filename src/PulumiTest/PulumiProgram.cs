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

    private static int _loggerCounter;

    private readonly IAutomationBackend _backend;
    private readonly Dictionary<string, string> _envVars;
    private IWorkspaceHandle? _workspace;
    private IStackHandle? _stack;

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

    private PulumiProgram(string workingDir, Options options, ILogger logger, IAutomationBackend backend)
    {
        WorkingDir = workingDir;
        Options = options;
        Logger = logger;
        _backend = backend;

        // Custom env vars from the Env() option take precedence over defaults.
        _envVars = new Dictionary<string, string>
        {
            ["PULUMI_BACKEND_URL"] = Environment.GetEnvironmentVariable("PULUMI_BACKEND_URL") ?? "",
            ["PULUMI_CONFIG_PASSPHRASE"] = string.IsNullOrEmpty(options.ConfigPassphrase)
                ? "correct horse battery staple"
                : options.ConfigPassphrase,
        };
        foreach (var kv in options.CustomEnv)
        {
            _envVars[kv.Key] = kv.Value;
        }
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
            var destination = program.CreateTempDir();
            program.CopyToInternal(destination);
            program.WorkingDir = destination;
        }

        await program.InitStackAsync(cancellationToken).ConfigureAwait(false);
        return program;
    }

    private static ILogger CreateDefaultLogger()
        => new ConsoleLogger($"PulumiProgram-{Interlocked.Increment(ref _loggerCounter)}");

    private string CreateTempDir()
    {
        var baseDir = string.IsNullOrEmpty(Options.TempDir)
            ? Path.Combine(Directory.GetCurrentDirectory(), "tmp")
            : Options.TempDir;
        Directory.CreateDirectory(baseDir);

        var tempPath = Path.Combine(baseDir, "programDir_" + Guid.NewGuid().ToString("N")[..8]);
        Logger.LogInformation("Creating temp directory {TempDir}", Path.GetFileName(tempPath));

        var sourceBase = Path.GetFileName(Path.GetFullPath(WorkingDir).TrimEnd(Path.DirectorySeparatorChar));
        var destination = Path.Combine(tempPath, sourceBase);
        Directory.CreateDirectory(destination);
        return destination;
    }

    private void CopyToInternal(string directory)
    {
        try
        {
            CopyDirectory(WorkingDir, directory, preserveTopLevel: null);
        }
        catch (IOException e)
        {
            throw new InvalidOperationException($"Error copying program to {directory}: {e.Message}", e);
        }
    }

    private async Task InitStackAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Creating local workspace...");
        _workspace = await _backend.CreateWorkspaceAsync(WorkingDir, WorkspaceEnvVars(), cancellationToken)
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
            _stack = await _workspace.CreateOrSelectStackAsync(stackName, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Logger.LogInformation("Skipping stack creation (SkipStackCreate=true)");
        }
    }

    /// <summary>Env vars to hand to the Automation API, with empty values dropped.</summary>
    private Dictionary<string, string>? WorkspaceEnvVars()
    {
        var result = _envVars.Where(kv => kv.Value != "").ToDictionary(kv => kv.Key, kv => kv.Value);
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Destroy and remove the stack. Register with your test framework's teardown.
    /// </summary>
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
        try
        {
            CopyDirectory(sourceDir, WorkingDir, preserveTopLevel: PreservedPaths);
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

    /// <summary>Get the environment variables for this workspace.</summary>
    public IReadOnlyDictionary<string, string> GetEnvVars() => new Dictionary<string, string>(_envVars);

    /// <summary>Copy the program to a new temporary directory.</summary>
    public Task<PulumiProgram> CopyToTempDirAsync(params Option[] opts)
        => CopyToAsync(CreateTempDir(), opts);

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

    private static void CopyDirectory(string sourceDir, string destDir, HashSet<string>? preserveTopLevel)
    {
        var source = new DirectoryInfo(sourceDir);
        if (!source.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        Directory.CreateDirectory(destDir);
        foreach (var entry in source.EnumerateFileSystemInfos())
        {
            if (preserveTopLevel is not null && preserveTopLevel.Contains(entry.Name))
            {
                continue;
            }

            var target = Path.Combine(destDir, entry.Name);
            if (entry.LinkTarget is not null)
            {
                if (File.Exists(target) || Directory.Exists(target))
                {
                    File.Delete(target);
                }

                File.CreateSymbolicLink(target, entry.LinkTarget);
            }
            else if (entry is DirectoryInfo dir)
            {
                CopyDirectory(dir.FullName, target, preserveTopLevel: null);
            }
            else
            {
                File.Copy(entry.FullName, target, overwrite: true);
            }
        }
    }
}
