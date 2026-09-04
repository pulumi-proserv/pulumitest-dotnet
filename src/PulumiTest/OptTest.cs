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

namespace PulumiTest;

/// <summary>
/// Configuration options for Pulumi testing.
/// </summary>
public sealed class Options
{
    /// <summary>The stack name to use when running the program under test.</summary>
    public string StackName { get; set; } = "test";

    /// <summary>Skip running <c>pulumi install</c> before running the program under test.</summary>
    public bool SkipInstall { get; set; }

    /// <summary>Skip creating the stack before running the program under test.</summary>
    public bool SkipStackCreate { get; set; }

    /// <summary>Run the program from its current location rather than copying to a temporary directory.</summary>
    public bool TestInPlace { get; set; }

    /// <summary>The temporary directory for copying the program under test.</summary>
    public string TempDir { get; set; } = Environment.GetEnvironmentVariable("PULUMITEST_TEMP_DIR") ?? "";

    /// <summary>The config passphrase to use when running the program under test.</summary>
    public string ConfigPassphrase { get; set; } = "correct horse battery staple";

    /// <summary>Use whatever backend configuration has been set via <c>pulumi login</c> or PULUMI_BACKEND_URL.</summary>
    public bool UseAmbientBackend { get; set; }

    /// <summary>Custom environment variables to use when running the program under test.</summary>
    public Dictionary<string, string> CustomEnv { get; set; } = new();

    /// <summary>Create a deep copy of the current options.</summary>
    public Options Copy() => new()
    {
        StackName = StackName,
        SkipInstall = SkipInstall,
        SkipStackCreate = SkipStackCreate,
        TestInPlace = TestInPlace,
        TempDir = TempDir,
        ConfigPassphrase = ConfigPassphrase,
        UseAmbientBackend = UseAmbientBackend,
        CustomEnv = new Dictionary<string, string>(CustomEnv),
    };

    /// <summary>Apply the given options to this instance, in order.</summary>
    public Options Apply(IEnumerable<Option> opts)
    {
        foreach (var opt in opts)
        {
            opt(this);
        }

        return this;
    }
}

/// <summary>
/// An option is a delegate that mutates an <see cref="Options"/> instance.
/// </summary>
public delegate void Option(Options options);

/// <summary>
/// Test options for the Pulumi testing framework.
/// </summary>
/// <remarks>
/// Matches the Go providertest/opttest naming convention. Options control
/// how <see cref="PulumiProgram"/> initializes and runs Pulumi programs.
/// </remarks>
public static class OptTest
{
    /// <summary>Create a new <see cref="Options"/> instance with default values.</summary>
    public static Options DefaultOptions() => new();

    /// <summary>Set the stack name to use when running the program under test.</summary>
    public static Option StackName(string name) => o => o.StackName = name;

    /// <summary>Skip running <c>pulumi install</c> before running the program under test.</summary>
    public static Option SkipInstall() => o => o.SkipInstall = true;

    /// <summary>Skip creating the stack before running the program under test.</summary>
    public static Option SkipStackCreate() => o => o.SkipStackCreate = true;

    /// <summary>Run the program from its current location, rather than copying to a temporary directory.</summary>
    public static Option TestInPlace() => o => o.TestInPlace = true;

    /// <summary>Set the temporary directory for copying the program under test.</summary>
    public static Option TempDir(string directory) => o => o.TempDir = directory;

    /// <summary>Set the config passphrase to use when running the program under test.</summary>
    public static Option ConfigPassphrase(string passphrase) => o => o.ConfigPassphrase = passphrase;

    /// <summary>Use whatever backend configuration has been set via <c>pulumi login</c> or PULUMI_BACKEND_URL.</summary>
    public static Option UseAmbientBackend() => o => o.UseAmbientBackend = true;

    /// <summary>Set a custom environment variable to use when running the program under test.</summary>
    public static Option Env(string key, string value) => o => o.CustomEnv[key] = value;
}
