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
using Pulumi.Automation;

namespace PulumiTest;

/// <summary>
/// Thrown when a result assertion such as <see cref="UpdateResult.HasNoChanges"/> fails.
/// Any test framework reports an unhandled exception as a test failure.
/// </summary>
public class PulumiTestAssertionException : Exception
{
    /// <summary>Create a new assertion exception with the given message.</summary>
    public PulumiTestAssertionException(string message) : base(message)
    {
    }
}

/// <summary>Helper for filtering operation counts.</summary>
public sealed class ChangeSummary
{
    /// <summary>The raw operation counts.</summary>
    public IReadOnlyDictionary<OperationType, int> Changes { get; }

    /// <summary>Wrap the given operation counts.</summary>
    public ChangeSummary(IReadOnlyDictionary<OperationType, int> changes)
    {
        Changes = changes;
    }

    /// <summary>Return the counts whose operation is not one of <paramref name="opTypes"/>.</summary>
    public IReadOnlyDictionary<OperationType, int> WhereOpNotEquals(params OperationType[] opTypes)
        => Changes.Where(kv => !opTypes.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>Return the counts whose operation is one of <paramref name="opTypes"/>.</summary>
    public IReadOnlyDictionary<OperationType, int> WhereOpEquals(params OperationType[] opTypes)
        => Changes.Where(kv => opTypes.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
}

internal static class ResultAssertions
{
    private static readonly OperationType[] DeleteOps =
    {
        OperationType.Delete,
        OperationType.DeleteReplaced,
    };

    private static readonly OperationType[] ReplacementOps =
    {
        OperationType.Replace,
        OperationType.CreateReplacement,
        OperationType.DeleteReplaced,
        OperationType.DiscardReplaced,
        OperationType.ImportReplacement,
        OperationType.ReadReplacement,
    };

    private static string Format(IReadOnlyDictionary<OperationType, int> ops)
        => "{" + string.Join(", ", ops.Select(kv => $"{kv.Key}: {kv.Value}")) + "}";

    public static void NoDeletes(IReadOnlyDictionary<OperationType, int> changes, string stdout)
    {
        var deletes = new ChangeSummary(changes).WhereOpEquals(DeleteOps);
        if (deletes.Count > 0)
        {
            throw new PulumiTestAssertionException($"expected no deletes, got {Format(deletes)}\n{stdout}");
        }
    }

    public static void NoChanges(IReadOnlyDictionary<OperationType, int> changes, string stdout)
    {
        var unexpected = new ChangeSummary(changes).WhereOpNotEquals(OperationType.Same);
        if (unexpected.Count > 0)
        {
            throw new PulumiTestAssertionException($"expected no changes, got {Format(unexpected)}\n{stdout}");
        }
    }

    public static void NoReplacements(IReadOnlyDictionary<OperationType, int> changes, string stdout)
    {
        var replacements = new ChangeSummary(changes).WhereOpEquals(ReplacementOps);
        if (replacements.Count > 0)
        {
            throw new PulumiTestAssertionException($"expected no replacements, got {Format(replacements)}\n{stdout}");
        }
    }
}

/// <summary>Result of <c>pulumi preview</c> with assertion helpers.</summary>
public sealed class PreviewResult
{
    /// <summary>The underlying Automation API result, when produced by a real preview.</summary>
    public Pulumi.Automation.PreviewResult? RawResult { get; }

    /// <summary>Operation counts yielded by the preview.</summary>
    public IReadOnlyDictionary<OperationType, int> ChangeSummary { get; }

    /// <summary>Standard output of the preview.</summary>
    public string StandardOutput { get; }

    /// <summary>Wrap an Automation API preview result.</summary>
    public PreviewResult(Pulumi.Automation.PreviewResult result)
        : this(result.ChangeSummary, result.StandardOutput)
    {
        RawResult = result;
    }

    internal PreviewResult(IReadOnlyDictionary<OperationType, int> changeSummary, string standardOutput)
    {
        ChangeSummary = changeSummary;
        StandardOutput = standardOutput;
    }

    /// <summary>Throw unless the preview contains no delete operations.</summary>
    public void HasNoDeletes() => ResultAssertions.NoDeletes(ChangeSummary, StandardOutput);

    /// <summary>Throw unless the preview contains only unchanged resources.</summary>
    public void HasNoChanges() => ResultAssertions.NoChanges(ChangeSummary, StandardOutput);

    /// <summary>Throw unless the preview contains no replacement operations.</summary>
    public void HasNoReplacements() => ResultAssertions.NoReplacements(ChangeSummary, StandardOutput);
}

/// <summary>Result of <c>pulumi refresh</c> with assertion helpers.</summary>
public sealed class RefreshResult
{
    /// <summary>The underlying Automation API result, when produced by a real refresh.</summary>
    public Pulumi.Automation.UpdateResult? RawResult { get; }

    /// <summary>The update summary, when available.</summary>
    public UpdateSummary? Summary => RawResult?.Summary;

    /// <summary>Operation counts recorded by the refresh, or null when the summary has none.</summary>
    public IReadOnlyDictionary<OperationType, int>? ChangeSummary { get; }

    /// <summary>Standard output of the refresh.</summary>
    public string StandardOutput { get; }

    /// <summary>Wrap an Automation API refresh result.</summary>
    public RefreshResult(Pulumi.Automation.UpdateResult result)
        : this(result.Summary.ResourceChanges, result.StandardOutput)
    {
        RawResult = result;
    }

    internal RefreshResult(IReadOnlyDictionary<OperationType, int>? changeSummary, string standardOutput)
    {
        ChangeSummary = changeSummary;
        StandardOutput = standardOutput;
    }

    /// <summary>Throw unless the refresh recorded only unchanged resources.</summary>
    public void HasNoChanges()
    {
        if (ChangeSummary is null)
        {
            return;
        }

        ResultAssertions.NoChanges(ChangeSummary, StandardOutput);
    }
}

/// <summary>Result of <c>pulumi up</c> with assertion helpers.</summary>
public sealed class UpdateResult
{
    /// <summary>The underlying Automation API result, when produced by a real update.</summary>
    public UpResult? RawResult { get; }

    /// <summary>Stack outputs.</summary>
    public IImmutableDictionary<string, OutputValue> Outputs { get; }

    /// <summary>The update summary, when available.</summary>
    public UpdateSummary? Summary => RawResult?.Summary;

    /// <summary>Operation counts recorded by the update, or null when the summary has none.</summary>
    public IReadOnlyDictionary<OperationType, int>? ChangeSummary { get; }

    /// <summary>Standard output of the update.</summary>
    public string StandardOutput { get; }

    /// <summary>Wrap an Automation API up result.</summary>
    public UpdateResult(UpResult result)
        : this(result.Outputs, result.Summary.ResourceChanges, result.StandardOutput)
    {
        RawResult = result;
    }

    internal UpdateResult(
        IImmutableDictionary<string, OutputValue> outputs,
        IReadOnlyDictionary<OperationType, int>? changeSummary,
        string standardOutput)
    {
        Outputs = outputs;
        ChangeSummary = changeSummary;
        StandardOutput = standardOutput;
    }

    /// <summary>Throw unless the update contains no delete operations.</summary>
    public void HasNoDeletes()
    {
        if (ChangeSummary is null)
        {
            return;
        }

        ResultAssertions.NoDeletes(ChangeSummary, StandardOutput);
    }

    /// <summary>Throw unless the update contains only unchanged resources.</summary>
    public void HasNoChanges()
    {
        if (ChangeSummary is null)
        {
            return;
        }

        ResultAssertions.NoChanges(ChangeSummary, StandardOutput);
    }

    /// <summary>Throw unless the update contains no replacement operations.</summary>
    public void HasNoReplacements()
    {
        if (ChangeSummary is null)
        {
            return;
        }

        ResultAssertions.NoReplacements(ChangeSummary, StandardOutput);
    }
}
