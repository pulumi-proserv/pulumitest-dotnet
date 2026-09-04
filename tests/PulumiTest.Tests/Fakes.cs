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

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Pulumi.Automation;

namespace PulumiTest.Tests;

/// <summary>In-memory stand-in for the Automation API. Records every call.</summary>
internal sealed class FakeBackend : IAutomationBackend
{
    public List<(string WorkDir, IDictionary<string, string>? Env)> WorkspaceCalls { get; } = new();

    public FakeWorkspace Workspace { get; } = new();

    public Task<IWorkspaceHandle> CreateWorkspaceAsync(
        string workDir,
        IDictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        WorkspaceCalls.Add((workDir, environmentVariables));
        return Task.FromResult<IWorkspaceHandle>(Workspace);
    }
}

internal sealed class FakeWorkspace : IWorkspaceHandle
{
    public int InstallCalls { get; private set; }

    public List<string> StackNames { get; } = new();

    public FakeStack Stack { get; } = new();

    public LocalWorkspace? Workspace => null;

    public Task InstallAsync(CancellationToken cancellationToken)
    {
        InstallCalls++;
        return Task.CompletedTask;
    }

    public Task<IStackHandle> CreateOrSelectStackAsync(string stackName, CancellationToken cancellationToken)
    {
        StackNames.Add(stackName);
        Stack.Name = stackName;
        return Task.FromResult<IStackHandle>(Stack);
    }
}

internal sealed class FakeStack : IStackHandle
{
    public string Name { get; set; } = "test";

    public WorkspaceStack? Stack => null;

    public int UpCalls { get; private set; }

    public int PreviewCalls { get; private set; }

    public int RefreshCalls { get; private set; }

    public int DestroyCalls { get; private set; }

    public int RemoveCalls { get; private set; }

    public List<string> Environments { get; } = new();

    public Exception? DestroyError { get; set; }

    public Task<UpdateResult> UpAsync(CancellationToken cancellationToken)
    {
        UpCalls++;
        return Task.FromResult(new UpdateResult(
            ImmutableDictionary<string, OutputValue>.Empty,
            new Dictionary<OperationType, int> { [OperationType.Create] = 1 },
            "up output"));
    }

    public Task<PreviewResult> PreviewAsync(CancellationToken cancellationToken)
    {
        PreviewCalls++;
        return Task.FromResult(new PreviewResult(new Dictionary<OperationType, int>(), "preview output"));
    }

    public Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        RefreshCalls++;
        return Task.FromResult(new RefreshResult(null, "refresh output"));
    }

    public Task DestroyAsync(CancellationToken cancellationToken)
    {
        DestroyCalls++;
        return DestroyError is null ? Task.CompletedTask : Task.FromException(DestroyError);
    }

    public Task RemoveAsync(CancellationToken cancellationToken)
    {
        RemoveCalls++;
        return Task.CompletedTask;
    }

    public Task AddEnvironmentsAsync(IEnumerable<string> environments, CancellationToken cancellationToken)
    {
        Environments.AddRange(environments);
        return Task.CompletedTask;
    }
}

/// <summary>Captures log messages so tests can assert on them.</summary>
internal sealed class CapturingLogger : ILogger
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception), exception));
}
