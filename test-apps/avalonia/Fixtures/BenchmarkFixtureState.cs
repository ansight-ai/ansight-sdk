#if ANSIGHT_BENCHMARK_TOOLS
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Ansight.Network;

namespace Ansight.Avalonia.Benchmarks;

internal sealed class BenchmarkFixtureState
{
    internal const int BinarySize = 262144;
    internal const string FixtureText = "Ansight Avalonia benchmark fixture\nUTF-8: café ✓\n";
    private readonly Assembly applicationAssembly;
    private readonly object gate = new object();
    private readonly List<FileStream> openFiles = new List<FileStream>();
    private readonly HttpClient httpClient;
#if ANDROID
    private readonly List<Java.Lang.Object> javaReferences = new List<Java.Lang.Object>();
#endif

    internal BenchmarkFixtureState(Assembly applicationAssembly)
    {
        this.applicationAssembly = applicationAssembly;
        RootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ansight-benchmark-fixtures");
        httpClient = new HttpClient(new AnsightHttpMessageHandler(options => options.WithBinaryBodies().WithMaximumBodyBytes(BinarySize)))
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    internal string RootPath { get; }
    internal string BinaryPath => Path.Combine(RootPath, "binary.bin");
    internal BenchmarkReflectionModel Model { get; } = new BenchmarkReflectionModel();

    internal JsonObject Prepare()
    {
        lock (gate)
        {
            ReleasePressure();
            Directory.CreateDirectory(RootPath);
            File.WriteAllText(Path.Combine(RootPath, "fixture.txt"), FixtureText, new UTF8Encoding(false));
            File.WriteAllBytes(BinaryPath, CreateBinary());

            using var database = applicationAssembly.GetManifestResourceStream("AnsightBenchmark.database.db")
                ?? throw new InvalidOperationException("The benchmark database resource is missing.");
            using (var destination = File.Create(Path.Combine(RootPath, "fixture.db")))
            {
                database.CopyTo(destination);
            }

            Model.Counter = 0;
            Model.Label = "prepared";
            return Snapshot();
        }
    }

    internal JsonObject Snapshot()
    {
        lock (gate)
        {
            var exists = File.Exists(BinaryPath);
            return new JsonObject
            {
                ["rootPath"] = RootPath,
                ["prepared"] = exists,
                ["binarySize"] = exists ? new FileInfo(BinaryPath).Length : 0,
                ["binarySha256"] = exists ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(BinaryPath))).ToLowerInvariant() : null,
                ["openFixtureHandles"] = openFiles.Count,
#if ANDROID
                ["heldJniReferences"] = javaReferences.Count,
#else
                ["heldJniReferences"] = 0,
#endif
                ["counter"] = Model.Counter,
                ["label"] = Model.Label
            };
        }
    }

    internal JsonObject SetPressure(bool enabled)
    {
        lock (gate)
        {
            ReleasePressure();
            if (enabled)
            {
                if (!File.Exists(BinaryPath))
                {
                    throw new InvalidOperationException("Call benchmark.prepare before enabling pressure.");
                }

                for (var index = 0; index < 32; index++)
                {
                    openFiles.Add(File.Open(BinaryPath, FileMode.Open, FileAccess.Read, FileShare.Read));
#if ANDROID
                    javaReferences.Add(new BenchmarkJavaReference());
#endif
                }
            }

            return Snapshot();
        }
    }

    internal async Task<JsonObject> RequestNetwork(string scenario)
    {
#if ANDROID
        const string baseUrl = "http://10.0.2.2:48733";
#else
        const string baseUrl = "http://127.0.0.1:48733";
#endif
        var requestId = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(scenario == "echo" ? HttpMethod.Post : HttpMethod.Get, $"{baseUrl}/{scenario}");
        request.Headers.Add("X-Ansight-Benchmark", requestId);
        if (scenario == "echo")
        {
            request.Content = new StringContent("{\"fixture\":\"avalonia\",\"value\":42}", Encoding.UTF8, "application/json");
        }

        using var response = await httpClient.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return new JsonObject
        {
            ["requestId"] = requestId,
            ["scenario"] = scenario,
            ["statusCode"] = (int)response.StatusCode,
            ["bodyBytes"] = bytes.Length,
            ["bodySha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            ["bodyText"] = scenario == "binary" ? null : Encoding.UTF8.GetString(bytes)
        };
    }

    private void ReleasePressure()
    {
        foreach (var file in openFiles)
        {
            file.Dispose();
        }

        openFiles.Clear();
#if ANDROID
        foreach (var reference in javaReferences)
        {
            reference.Dispose();
        }

        javaReferences.Clear();
#endif
    }

    private static byte[] CreateBinary()
    {
        var bytes = new byte[BinarySize];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)((index * 31 + 17) % 256);
        }

        return bytes;
    }
}

public sealed class BenchmarkReflectionModel
{
    public int Counter { get; set; }
    public string Label { get; set; } = "initial";

    public int Increment(int amount)
    {
        Counter += amount;
        return Counter;
    }
}

#if ANDROID
internal sealed class BenchmarkJavaReference : Java.Lang.Object
{
}
#endif
#endif
