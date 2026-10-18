# Expose a scene through custom tools

Run `dotnet run --project src/dotnet/samples/Ansight.DomainTools.Sample` from the SDK repository. The program exercises the real tool bridge without a device: read, write, malformed arguments, denied writes, unknown entity, and scene readiness.

Copy `SceneDiagnostics` into a local development app and register `builder.AddTools(scene.CreateTools())` inside its existing Ansight configuration. Choose `WithReadWriteToolAccess()` when mutation is intended. Use the app's existing Debug/build gating for custom tools.

The sample uses `JsonTool.Create`, explicit schemas, and a readiness callback. A dedicated `IJsonTool` class is equally supported and needs only `ExecuteAsync`; its legacy adapter is inherited. For a real engine, replace the lock-backed model access with the engine's owning-thread dispatcher. Do not hold a lock across an awaited dispatch.

After pairing, call `ansight app call <session> scene.snapshot --arguments '{}' --json`, then `scene.set_visible` with `{"id":"cube","visible":false}`. Re-read the state to verify the mutation. `PublishSnapshotAsync` shows how an app event publishes durable JSON evidence without a remote request; it requires a connected host supporting artifact publication.
