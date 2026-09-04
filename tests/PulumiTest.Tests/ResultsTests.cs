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

namespace PulumiTest.Tests;

public class ResultsTests
{
    private static Dictionary<OperationType, int> Ops(params (OperationType Op, int Count)[] entries)
        => entries.ToDictionary(e => e.Op, e => e.Count);

    private static UpdateResult Up(Dictionary<OperationType, int>? changes, string stdout = "")
        => new(ImmutableDictionary<string, OutputValue>.Empty, changes, stdout);

    [Fact]
    public void ChangeSummary_WhereOpNotEquals_FiltersGivenOps()
    {
        var cs = new ChangeSummary(Ops((OperationType.Same, 3), (OperationType.Create, 1)));
        Assert.Equal(Ops((OperationType.Create, 1)), cs.WhereOpNotEquals(OperationType.Same));
    }

    [Fact]
    public void ChangeSummary_WhereOpNotEquals_EmptyWhenAllFiltered()
    {
        var cs = new ChangeSummary(Ops((OperationType.Same, 5)));
        Assert.Empty(cs.WhereOpNotEquals(OperationType.Same));
    }

    [Fact]
    public void ChangeSummary_WhereOpEquals_KeepsGivenOps()
    {
        var cs = new ChangeSummary(Ops((OperationType.Same, 3), (OperationType.Delete, 1), (OperationType.DeleteReplaced, 2)));
        Assert.Equal(
            Ops((OperationType.Delete, 1), (OperationType.DeleteReplaced, 2)),
            cs.WhereOpEquals(OperationType.Delete, OperationType.DeleteReplaced));
    }

    [Fact]
    public void Preview_HasNoChanges_PassesWhenOnlySame()
    {
        new PreviewResult(Ops((OperationType.Same, 3)), "").HasNoChanges();
    }

    [Fact]
    public void Preview_HasNoChanges_FailsWithOtherOps()
    {
        var result = new PreviewResult(Ops((OperationType.Same, 3), (OperationType.Create, 1)), "output");
        var ex = Assert.Throws<PulumiTestAssertionException>(() => result.HasNoChanges());
        Assert.Contains("expected no changes", ex.Message);
        Assert.Contains("Create: 1", ex.Message);
        Assert.Contains("output", ex.Message);
    }

    [Fact]
    public void Preview_HasNoDeletes_PassesWithoutDeleteOps()
    {
        new PreviewResult(Ops((OperationType.Same, 3), (OperationType.Create, 1)), "").HasNoDeletes();
    }

    [Fact]
    public void Preview_HasNoDeletes_FailsWithDeleteOps()
    {
        var result = new PreviewResult(Ops((OperationType.Same, 3), (OperationType.Delete, 1)), "output");
        var ex = Assert.Throws<PulumiTestAssertionException>(() => result.HasNoDeletes());
        Assert.Contains("expected no deletes", ex.Message);
    }

    [Fact]
    public void Preview_HasNoReplacements_PassesWithoutReplacementOps()
    {
        new PreviewResult(Ops((OperationType.Same, 3), (OperationType.Update, 1)), "").HasNoReplacements();
    }

    [Fact]
    public void Preview_HasNoReplacements_FailsWithReplacementOps()
    {
        var result = new PreviewResult(Ops((OperationType.Same, 3), (OperationType.Replace, 1)), "");
        var ex = Assert.Throws<PulumiTestAssertionException>(() => result.HasNoReplacements());
        Assert.Contains("expected no replacements", ex.Message);
    }

    [Fact]
    public void Update_HasNoChanges_PassesWhenChangeSummaryIsNull()
    {
        Up(null).HasNoChanges();
    }

    [Fact]
    public void Update_HasNoChanges_PassesWhenOnlySame()
    {
        Up(Ops((OperationType.Same, 5))).HasNoChanges();
    }

    [Fact]
    public void Update_HasNoChanges_FailsWithOtherOps()
    {
        var result = Up(Ops((OperationType.Same, 3), (OperationType.Update, 1)), "output");
        var ex = Assert.Throws<PulumiTestAssertionException>(() => result.HasNoChanges());
        Assert.Contains("expected no changes", ex.Message);
    }

    [Fact]
    public void Update_HasNoDeletes_PassesWhenChangeSummaryIsNull()
    {
        Up(null).HasNoDeletes();
    }

    [Fact]
    public void Update_HasNoDeletes_FailsWithDeleteReplaced()
    {
        var result = Up(Ops((OperationType.DeleteReplaced, 1)));
        Assert.Throws<PulumiTestAssertionException>(() => result.HasNoDeletes());
    }

    [Fact]
    public void Update_HasNoReplacements_PassesWhenChangeSummaryIsNull()
    {
        Up(null).HasNoReplacements();
    }

    [Fact]
    public void Update_HasNoReplacements_FailsWithCreateReplacement()
    {
        var result = Up(Ops((OperationType.CreateReplacement, 1)));
        Assert.Throws<PulumiTestAssertionException>(() => result.HasNoReplacements());
    }

    [Fact]
    public void Update_ExposesOutputsAndChangeSummary()
    {
        var result = Up(Ops((OperationType.Create, 1)), "out");
        Assert.Empty(result.Outputs);
        Assert.Equal(Ops((OperationType.Create, 1)), result.ChangeSummary);
        Assert.Equal("out", result.StandardOutput);
        Assert.Null(result.RawResult);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void Refresh_HasNoChanges_PassesWhenChangeSummaryIsNull()
    {
        new RefreshResult(null, "").HasNoChanges();
    }

    [Fact]
    public void Refresh_HasNoChanges_PassesWhenOnlySame()
    {
        new RefreshResult(Ops((OperationType.Same, 2)), "").HasNoChanges();
    }

    [Fact]
    public void Refresh_HasNoChanges_FailsWithOtherOps()
    {
        var result = new RefreshResult(Ops((OperationType.Same, 2), (OperationType.Update, 1)), "output");
        var ex = Assert.Throws<PulumiTestAssertionException>(() => result.HasNoChanges());
        Assert.Contains("expected no changes", ex.Message);
    }

    [Fact]
    public void Refresh_ExposesChangeSummary()
    {
        var result = new RefreshResult(Ops((OperationType.Same, 2)), "");
        Assert.Equal(Ops((OperationType.Same, 2)), result.ChangeSummary);
        Assert.Null(result.Summary);
    }
}
