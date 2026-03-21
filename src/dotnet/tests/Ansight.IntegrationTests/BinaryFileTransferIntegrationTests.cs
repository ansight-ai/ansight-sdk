using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Ansight.IntegrationTests.Support;
using Ansight.Pairing;
using Ansight.Tools;
using Ansight.Tools.FileSystem;

namespace Ansight.IntegrationTests;

public sealed class BinaryFileTransferIntegrationTests
{
    [Fact]
    public async Task BeginBinaryDownloadTool_Execute_StreamsBinaryFramesToHost()
    {
        var logCollector = new TestLogCallback();
        Logger.RegisterCallback(logCollector);
        await using var server = await LoopbackWebSocketServer.StartAsync();
        using var webSocket = await ConnectAsync(server.WebSocketUri);
        using var transport = new PairingSessionTransport();
        transport.Attach(webSocket);

        var transferHub = new PairingBinaryTransferHub();
        transferHub.AttachTransport(transport);

        var rootPath = Path.Combine(Path.GetTempPath(), "Ansight.IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            var fileBytes = Enumerable.Range(0, 2050).Select(value => (byte)(value % 251)).ToArray();
            var filePath = Path.Combine(rootPath, "payload.bin");
            await File.WriteAllBytesAsync(filePath, fileBytes);

            var tool = new BeginBinaryDownloadTool(
                FileSystemToolsOptions.CreateBuilder()
                    .AddRoot("workspace", rootPath)
                    .Build(),
                () => transferHub);
            const int chunkBytes = 1024;

            var result = await tool.Execute(new Dictionary<string, string>
            {
                ["root"] = "workspace",
                ["path"] = "payload.bin",
                ["chunkBytes"] = chunkBytes.ToString(),
                [ToolExecutionArgumentNames.RequestId] = "req_1"
            });

            Assert.True(result.IsSuccess, result.Message);

            var payload = Assert.IsType<JsonObject>(result.Payload);
            var transferId = payload["transferId"]!.GetValue<string>();
            Assert.Equal("websocket_binary", payload["deliveryMode"]?.GetValue<string>());
            Assert.Equal(PairingFileTransferWireProtocol.ProtocolName, payload["wireProtocol"]?.GetValue<string>());
            Assert.Equal("req_1", payload["downloadId"]?.GetValue<string>());
            Assert.Equal(chunkBytes, payload["chunkBytes"]?.GetValue<int>());

            Assert.True(transferHub.TryStartQueuedTransfer("req_1"));
            try
            {
                await server.WaitForBinaryMessagesAsync(4, TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Binary frames were not observed. Logs:{Environment.NewLine}{string.Join(Environment.NewLine, logCollector.Messages)}{Environment.NewLine}{exception}");
            }

            Assert.Collection(
                server.BinaryMessages,
                frame => AssertFrame(frame, transferId, expectedType: PairingFileTransferFrameType.Chunk, expectedSequence: 0, expectedOffsetBytes: 0, expectedPayload: fileBytes[..chunkBytes]),
                frame => AssertFrame(frame, transferId, expectedType: PairingFileTransferFrameType.Chunk, expectedSequence: 1, expectedOffsetBytes: chunkBytes, expectedPayload: fileBytes[chunkBytes..(2 * chunkBytes)]),
                frame => AssertFrame(frame, transferId, expectedType: PairingFileTransferFrameType.Chunk, expectedSequence: 2, expectedOffsetBytes: 2 * chunkBytes, expectedPayload: fileBytes[(2 * chunkBytes)..2050]),
                frame => AssertFrame(frame, transferId, expectedType: PairingFileTransferFrameType.Complete, expectedSequence: 3, expectedOffsetBytes: 2050, expectedPayload: Array.Empty<byte>()));
        }
        finally
        {
            transferHub.DetachTransport(transport);
            await transport.CloseAsync(CancellationToken.None);

            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }

            Logger.RemoveCallback(logCollector);
        }
    }

    private static void AssertFrame(
        byte[] frame,
        string expectedTransferId,
        PairingFileTransferFrameType expectedType,
        int expectedSequence,
        long expectedOffsetBytes,
        byte[] expectedPayload)
    {
        Assert.True(frame.Length >= PairingFileTransferWireProtocol.HeaderSize);
        Assert.Equal("ASFT", Encoding.ASCII.GetString(frame, 0, 4));
        Assert.Equal((byte)expectedType, frame[5]);
        Assert.Equal(expectedTransferId, Encoding.ASCII.GetString(frame, 8, 32));
        Assert.Equal(expectedSequence, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(40, 4)));
        Assert.Equal(expectedOffsetBytes, BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(44, 8)));
        Assert.Equal(expectedPayload.Length, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(52, 4)));
        Assert.Equal(expectedPayload, frame[PairingFileTransferWireProtocol.HeaderSize..]);
    }

    private static async Task<ClientWebSocket> ConnectAsync(Uri webSocketUri)
    {
        var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(webSocketUri, CancellationToken.None);
        return webSocket;
    }

    private sealed class TestLogCallback : ILogCallback
    {
        public string Name => nameof(TestLogCallback);

        public List<string> Messages { get; } = new();

        public void Error(string message) => Messages.Add($"ERROR {message}");

        public void Warning(string message) => Messages.Add($"WARN {message}");

        public void Info(string message) => Messages.Add($"INFO {message}");

        public void Exception(Exception exception) => Messages.Add($"EX {exception.Message}");
    }
}
