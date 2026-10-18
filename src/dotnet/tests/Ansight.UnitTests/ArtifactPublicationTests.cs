using System.Text;
using System.Text.Json.Nodes;
using Ansight.Artifacts;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class ArtifactPublicationTests
{
    private static ArtifactResult Artifact(IArtifactPayload? payload = null) => new(
        new ArtifactMetadata("model", "scene", "Loaded model", "model", "model/gltf-binary", "cube.glb"),
        payload ?? ArtifactPayload.FromText("model bytes"));

    [Fact]
    public async Task PublicationWaitsForCommitAndDoesNotRequireAToolRequest()
    {
        var controls = new List<string>();
        var frames = new List<byte[]>();
        var result = await ArtifactPublication.SendAsync(Artifact(), () => true,
            (action, payload, _) =>
            {
                controls.Add(action);
                if (action == ArtifactPublishProtocol.BeginAction)
                {
                    Assert.Equal("scene", payload["artifact"]!["providerId"]!.GetValue<string>());
                    Assert.Null(payload["requestId"]);
                }
                if (action == ArtifactPublishProtocol.CommitAction) Assert.Equal(2, frames.Count);
                return Task.FromResult(OperationResult.FromSuccess("retained"));
            }, (bytes, _) => { frames.Add(bytes.ToArray()); return Task.FromResult(OperationResult.FromSuccess("sent")); }, default);
        Assert.True(result.Success);
        Assert.Equal([ArtifactPublishProtocol.BeginAction, ArtifactPublishProtocol.CommitAction], controls);
        Assert.Equal("model bytes", Encoding.UTF8.GetString(frames[0][56..]));
        Assert.Equal(2, frames[1][5]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedOrFailedHostCommitNeverReportsSuccess(bool failCommit)
    {
        var actions = new List<string>();
        var frames = 0;
        var result = await ArtifactPublication.SendAsync(Artifact(), () => true,
            (action, _, _) =>
            {
                actions.Add(action);
                return Task.FromResult(!failCommit || action == ArtifactPublishProtocol.CommitAction
                    ? OperationResult.FromFailure("rejected") : OperationResult.FromSuccess("ok"));
            }, (_, _) => { frames++; return Task.FromResult(OperationResult.FromSuccess("sent")); }, default);
        Assert.False(result.Success);
        Assert.Equal(failCommit ? 2 : 0, frames);
        Assert.Equal(failCommit ? ArtifactPublishProtocol.AbortAction : ArtifactPublishProtocol.BeginAction, actions.Last());
    }

    [Fact]
    public async Task DisconnectedDoesNotOpenPayload()
    {
        var opened = false;
        var result = await ArtifactPublication.SendAsync(Artifact(ArtifactPayload.FromStream(_ =>
        { opened = true; throw new Exception(); })), () => false,
            (_, _, _) => throw new Exception(), (_, _) => throw new Exception(), default);
        Assert.False(result.Success);
        Assert.False(opened);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAbortsAndDisposesStream(bool afterComplete)
    {
        using var cancel = new CancellationTokenSource();
        var stream = new MemoryStream(new byte[afterComplete ? 0 : 65537]);
        var actions = new List<string>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArtifactPublication.SendAsync(
            Artifact(ArtifactPayload.FromStream(_ => ValueTask.FromResult<Stream>(stream))), () => true,
            (action, _, _) => { actions.Add(action); return Task.FromResult(OperationResult.FromSuccess("ok")); },
            (_, _) => { cancel.Cancel(); return Task.FromResult(OperationResult.FromSuccess("sent")); }, cancel.Token));
        Assert.False(stream.CanRead);
        Assert.DoesNotContain(ArtifactPublishProtocol.CommitAction, actions);
        Assert.Equal(ArtifactPublishProtocol.AbortAction, actions.Last());
    }
}
