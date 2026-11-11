#if ANSIGHT_BENCHMARK_TOOLS
using System.Net.Http;
using Ansight.Tools;

namespace Ansight.Avalonia.Benchmarks;

internal abstract class BenchmarkTool : ITool
{
    public string Category => "benchmark";
    public string Keywords => "avalonia benchmark synthetic fixture";
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract ToolPolicy Policy { get; }
    public abstract ToolSchema ArgumentsSchema { get; }
    public abstract ToolSchema ResultSchema { get; }

    public async Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        try
        {
            return await Run(arguments);
        }
        catch (ArgumentException exception)
        {
            return ToolResult.Failure(exception.Message, "invalid_argument");
        }
        catch (InvalidOperationException exception)
        {
            return ToolResult.Failure(exception.Message, "fixture_not_ready");
        }
        catch (HttpRequestException exception)
        {
            return ToolResult.Failure(exception.Message, "fixture_network_failed");
        }
        catch (IOException exception)
        {
            return ToolResult.Failure(exception.Message, "fixture_io_failed");
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Failure("The benchmark request timed out.", "fixture_timeout");
        }
    }

    protected abstract Task<ToolResult> Run(IReadOnlyDictionary<string, string> arguments);
}
#endif
