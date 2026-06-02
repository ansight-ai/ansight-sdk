using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Ansight.Artifacts;
using Ansight.IntegrationTests.Support;
using Ansight.Pairing;
using Ansight.Tools;

namespace Ansight.IntegrationTests;

public sealed class ArtifactTransferIntegrationTests
{
    [Fact]
    public async Task RequestArtifactTool_Execute_StreamsArtifactFramesToHost()
    {
        await using var server = await LoopbackWebSocketServer.StartAsync();
        using var webSocket = await ConnectAsync(server.WebSocketUri);
        using var transport = new PairingSessionTransport();
        transport.Attach(webSocket);

        var transferHub = new PairingBinaryTransferHub();
        transferHub.AttachTransport(transport);

        try
        {
            var bytes = Enumerable.Range(0, 2050).Select(value => (byte)(value % 251)).ToArray();
            var provider = new TestArtifactProvider(bytes);
            var tool = new RequestArtifactTool(
                new ArtifactRegistry(new[] { provider }),
                () => transferHub);
            const int chunkBytes = 1024;

            var result = await tool.Execute(new Dictionary<string, string>
            {
                [ToolExecutionArgumentNames.RequestId] = "req_1",
                ["providerId"] = "app.snapshot",
                ["artifactId"] = "payload",
                ["chunkBytes"] = chunkBytes.ToString()
            });

            Assert.True(result.IsSuccess, result.Message);

            var payload = Assert.IsType<JsonObject>(result.Payload);
            var transferId = payload["transferId"]!.GetValue<string>();
            Assert.Equal("websocket_binary", payload["deliveryMode"]?.GetValue<string>());
            Assert.Equal(PairingFileTransferWireProtocol.ProtocolName, payload["wireProtocol"]?.GetValue<string>());
            Assert.Equal("req_1", payload["downloadId"]?.GetValue<string>());

            var artifact = Assert.IsType<JsonObject>(payload["artifact"]);
            Assert.Equal("payload", artifact["artifactId"]?.GetValue<string>());
            Assert.Equal("app.snapshot", artifact["providerId"]?.GetValue<string>());
            Assert.Equal(bytes.Length, artifact["sizeBytes"]?.GetValue<long>());

            Assert.True(transferHub.TryStartQueuedTransfer("req_1"));
            await server.WaitForBinaryMessagesAsync(4, TimeSpan.FromSeconds(5));

            Assert.Collection(
                server.BinaryMessages,
                frame => AssertFrame(frame, transferId, expectedType: PairingFileTransferFrameType.Chunk, expectedSequence: 0, expectedOffsetBytes: 0, expectedPayload: bytes[..chunkBytes]),
                frame => AssertFrame(frame, transferId, expectedType: PairingFileTransferFrameType.Chunk, expectedSequence: 1, expectedOffsetBytes: chunkBytes, expectedPayload: bytes[chunkBytes..(2 * chunkBytes)]),
                frame => AssertFrame(frame, transferId, expectedType: PairingFileTransferFrameType.Chunk, expectedSequence: 2, expectedOffsetBytes: 2 * chunkBytes, expectedPayload: bytes[(2 * chunkBytes)..2050]),
                frame => AssertFrame(frame, transferId, expectedType: PairingFileTransferFrameType.Complete, expectedSequence: 3, expectedOffsetBytes: 2050, expectedPayload: Array.Empty<byte>()));
        }
        finally
        {
            transferHub.DetachTransport(transport);
            await transport.CloseAsync(CancellationToken.None);
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

    private sealed class TestArtifactProvider : IArtifactProvider
    {
        private readonly byte[] bytes;

        public TestArtifactProvider(byte[] bytes)
        {
            this.bytes = bytes;
        }

        public ArtifactProviderDescriptor Descriptor { get; } = new(
            "app.snapshot",
            "Snapshot Provider",
            "Provides binary snapshots.",
            "diagnostics");

        public Task<IReadOnlyList<ArtifactDefinition>> QueryAsync(
            ArtifactQueryContext context,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ArtifactDefinition> definitions =
            [
                new ArtifactDefinition(
                    "payload",
                    "Payload",
                    "Binary payload.",
                    "binary",
                    "diagnostics",
                    new ArtifactContentDescriptor(new[] { "application/octet-stream" })
                    {
                        DefaultMimeType = "application/octet-stream",
                        SuggestedFileName = "payload.bin",
                        SupportsBinary = true
                    },
                    ToolSchema.Object(),
                    ArtifactToolSecurityProfiles.Request)
            ];

            return Task.FromResult(definitions);
        }

        public Task<ArtifactResult> CreateAsync(
            ArtifactRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = new ArtifactResult(
                new ArtifactMetadata(
                    request.ArtifactId,
                    request.ProviderId,
                    "Payload",
                    "binary",
                    "application/octet-stream",
                    "payload.bin"),
                ArtifactPayload.FromBytes(bytes));

            return Task.FromResult(result);
        }
    }
}
