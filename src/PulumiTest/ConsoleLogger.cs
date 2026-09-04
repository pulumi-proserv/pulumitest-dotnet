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

namespace PulumiTest;

/// <summary>
/// Minimal <see cref="ILogger"/> that writes to standard output, used when no
/// logger is supplied. Avoids a dependency on the console logging provider.
/// </summary>
internal sealed class ConsoleLogger : ILogger
{
    private readonly string _name;

    public ConsoleLogger(string name)
    {
        _name = name;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var writer = logLevel >= LogLevel.Error ? Console.Error : Console.Out;
        writer.WriteLine($"{logLevel.ToString().ToUpperInvariant()} - {_name} - {formatter(state, exception)}");
        if (exception is not null)
        {
            writer.WriteLine(exception.ToString());
        }
    }
}
