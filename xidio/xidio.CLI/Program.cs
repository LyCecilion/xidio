using Spectre.Console;
using System.Diagnostics.CodeAnalysis;
using xidio.Core.Abstractions;
using xidio.Core.Diagnostics;
using xidio.Core.Models;
#if WINDOWS
using xidio.Platform.Windows;
#elif LINUX
using xidio.Platform.Linux;
#elif MACOS
using xidio.Platform.macOS;
#endif

namespace xidio.CLI;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            ConsoleDiagnosticReporter.PrintBanner();

            var userScenario = await ConsoleDiagnosticQuestionnaire.AskAsync(cancellation.Token);
            var collector = new NetworkDiagnosticsCollector(CreatePlatformProvider());
            var report = await CollectWithProgressAsync(collector, userScenario, cancellation.Token);

            ConsoleDiagnosticReporter.Print(report);
            WaitForExitKey();
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]诊断已取消。[/]");
        }
    }

    private static Task<NetworkDiagnosticReport> CollectWithProgressAsync(
        NetworkDiagnosticsCollector collector,
        UserScenarioInfo userScenario,
        CancellationToken cancellationToken)
    {
        return AnsiConsole.Progress()
            .AutoClear(true)
            .Columns(
                new SpinnerColumn(),
                new PercentageColumn(),
                new ProgressBarColumn(),
                new TaskDescriptionColumn()
                )
            .StartAsync(async context =>
            {
                var task = context.AddTask("正在准备诊断环境", maxValue: 10);
                var progress = new InlineProgress<NetworkDiagnosticProgress>(state =>
                {
                    task.Description = state.Message;
                    task.MaxValue = state.TotalSteps;
                    task.Value = Math.Clamp(state.CompletedSteps, 0, state.TotalSteps);
                });

                return await collector.CollectAsync(userScenario, progress, cancellationToken);
            });
    }

    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The CLI keeps provider creation behind the cross-platform abstraction.")]
    private static IPlatformNetworkDiagnosticsProvider CreatePlatformProvider()
    {
#if WINDOWS
        return new WindowsPlatformNetworkDiagnosticsProvider();
#elif LINUX
        return new LinuxPlatformNetworkDiagnosticsProvider();
#elif MACOS
        return new MacPlatformNetworkDiagnosticsProvider();
#else
        return NoopPlatformNetworkDiagnosticsProvider.Instance;
#endif
    }

    private static void WaitForExitKey()
    {
        Console.WriteLine();
        AnsiConsole.MarkupLine("[grey]按任意键退出。[/]");
        Console.ReadKey(intercept: true);
    }

    private sealed class InlineProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value)
        {
            onReport(value);
        }
    }
}
