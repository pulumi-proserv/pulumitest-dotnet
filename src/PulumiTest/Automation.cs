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

using Pulumi.Automation;

namespace PulumiTest;

// The Automation API's LocalWorkspace, WorkspaceStack and result types are
// sealed with internal constructors, so PulumiProgram talks to the SDK
// through this thin seam. Tests substitute a fake; production uses
// LocalAutomationBackend below.

/// <summary>Creates workspaces. Internal seam over the Automation API.</summary>
internal interface IAutomationBackend
{
    Task<IWorkspaceHandle> CreateWorkspaceAsync(
        string workDir,
        IDictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken);
}

/// <summary>A local workspace and the operations PulumiProgram needs from it.</summary>
internal interface IWorkspaceHandle
{
    LocalWorkspace? Workspace { get; }

    Task InstallAsync(CancellationToken cancellationToken);

    Task<IStackHandle> CreateOrSelectStackAsync(string stackName, CancellationToken cancellationToken);
}

/// <summary>A stack and the operations PulumiProgram needs from it.</summary>
internal interface IStackHandle
{
    string Name { get; }

    WorkspaceStack? Stack { get; }

    Task<UpdateResult> UpAsync(CancellationToken cancellationToken);

    Task<PreviewResult> PreviewAsync(CancellationToken cancellationToken);

    Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken);

    Task DestroyAsync(CancellationToken cancellationToken);

    Task RemoveAsync(CancellationToken cancellationToken);

    Task AddEnvironmentsAsync(IEnumerable<string> environments, CancellationToken cancellationToken);
}

internal sealed class LocalAutomationBackend : IAutomationBackend
{
    public static readonly LocalAutomationBackend Instance = new();

    public async Task<IWorkspaceHandle> CreateWorkspaceAsync(
        string workDir,
        IDictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        // The SDK types its env var dictionary with nullable values.
        var sdkEnv = environmentVariables?.ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
        var workspace = await LocalWorkspace.CreateAsync(
            new LocalWorkspaceOptions
            {
                WorkDir = workDir,
                EnvironmentVariables = sdkEnv,
            },
            cancellationToken).ConfigureAwait(false);
        return new LocalWorkspaceHandle(workspace, sdkEnv);
    }

    private sealed class LocalWorkspaceHandle : IWorkspaceHandle
    {
        private readonly IDictionary<string, string?>? _environmentVariables;

        public LocalWorkspaceHandle(LocalWorkspace workspace, IDictionary<string, string?>? environmentVariables)
        {
            Workspace = workspace;
            _environmentVariables = environmentVariables;
        }

        public LocalWorkspace Workspace { get; }

        LocalWorkspace? IWorkspaceHandle.Workspace => Workspace;

        public Task InstallAsync(CancellationToken cancellationToken)
            => Workspace.InstallAsync(new InstallOptions(), cancellationToken);

        public async Task<IStackHandle> CreateOrSelectStackAsync(string stackName, CancellationToken cancellationToken)
        {
            var stack = await LocalWorkspace.CreateOrSelectStackAsync(
                new LocalProgramArgs(stackName, Workspace.WorkDir)
                {
                    EnvironmentVariables = _environmentVariables,
                },
                cancellationToken).ConfigureAwait(false);
            return new LocalStackHandle(stack);
        }
    }

    private sealed class LocalStackHandle : IStackHandle
    {
        public LocalStackHandle(WorkspaceStack stack)
        {
            Stack = stack;
        }

        public WorkspaceStack Stack { get; }

        WorkspaceStack? IStackHandle.Stack => Stack;

        public string Name => Stack.Name;

        public async Task<UpdateResult> UpAsync(CancellationToken cancellationToken)
            => new(await Stack.UpAsync(cancellationToken: cancellationToken).ConfigureAwait(false));

        public async Task<PreviewResult> PreviewAsync(CancellationToken cancellationToken)
            => new(await Stack.PreviewAsync(cancellationToken: cancellationToken).ConfigureAwait(false));

        public async Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken)
            => new(await Stack.RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false));

        public Task DestroyAsync(CancellationToken cancellationToken)
            => Stack.DestroyAsync(cancellationToken: cancellationToken);

        public Task RemoveAsync(CancellationToken cancellationToken)
            => Stack.Workspace.RemoveStackAsync(Stack.Name, cancellationToken);

        public Task AddEnvironmentsAsync(IEnumerable<string> environments, CancellationToken cancellationToken)
            => Stack.AddEnvironmentsAsync(environments, cancellationToken);
    }
}
