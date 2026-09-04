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

namespace PulumiTest.Tests;

public class OptTestTests
{
    [Fact]
    public void DefaultOptions_HaveSensibleDefaults()
    {
        var opts = OptTest.DefaultOptions();
        Assert.Equal("test", opts.StackName);
        Assert.False(opts.SkipInstall);
        Assert.False(opts.SkipStackCreate);
        Assert.False(opts.TestInPlace);
        Assert.Equal("correct horse battery staple", opts.ConfigPassphrase);
        Assert.False(opts.UseAmbientBackend);
        Assert.Empty(opts.CustomEnv);
    }

    [Fact]
    public void StackName_SetsStackName()
    {
        var opts = OptTest.DefaultOptions();
        OptTest.StackName("custom")(opts);
        Assert.Equal("custom", opts.StackName);
    }

    [Fact]
    public void SkipInstall_SetsSkipInstall()
    {
        var opts = OptTest.DefaultOptions();
        OptTest.SkipInstall()(opts);
        Assert.True(opts.SkipInstall);
    }

    [Fact]
    public void SkipStackCreate_SetsSkipStackCreate()
    {
        var opts = OptTest.DefaultOptions();
        OptTest.SkipStackCreate()(opts);
        Assert.True(opts.SkipStackCreate);
    }

    [Fact]
    public void TestInPlace_SetsTestInPlace()
    {
        var opts = OptTest.DefaultOptions();
        OptTest.TestInPlace()(opts);
        Assert.True(opts.TestInPlace);
    }

    [Fact]
    public void TempDir_SetsTempDir()
    {
        var opts = OptTest.DefaultOptions();
        OptTest.TempDir("/tmp/custom")(opts);
        Assert.Equal("/tmp/custom", opts.TempDir);
    }

    [Fact]
    public void ConfigPassphrase_SetsPassphrase()
    {
        var opts = OptTest.DefaultOptions();
        OptTest.ConfigPassphrase("secret")(opts);
        Assert.Equal("secret", opts.ConfigPassphrase);
    }

    [Fact]
    public void UseAmbientBackend_SetsUseAmbientBackend()
    {
        var opts = OptTest.DefaultOptions();
        OptTest.UseAmbientBackend()(opts);
        Assert.True(opts.UseAmbientBackend);
    }

    [Fact]
    public void Env_AddsCustomEnvironmentVariable()
    {
        var opts = OptTest.DefaultOptions();
        OptTest.Env("FOO", "bar")(opts);
        Assert.Equal(new Dictionary<string, string> { ["FOO"] = "bar" }, opts.CustomEnv);
    }

    [Fact]
    public void Copy_ProducesIndependentCopy()
    {
        var opts = OptTest.DefaultOptions();
        opts.CustomEnv["KEY"] = "val";
        var copied = opts.Copy();
        copied.CustomEnv["KEY2"] = "val2";
        Assert.DoesNotContain("KEY2", opts.CustomEnv.Keys);
        Assert.Equal(2, copied.CustomEnv.Count);
    }

    [Fact]
    public void Apply_AppliesMultipleOptionsInOrder()
    {
        var opts = OptTest.DefaultOptions().Apply(new[]
        {
            OptTest.StackName("prod"),
            OptTest.SkipInstall(),
            OptTest.TestInPlace(),
        });
        Assert.Equal("prod", opts.StackName);
        Assert.True(opts.SkipInstall);
        Assert.True(opts.TestInPlace);
    }
}
