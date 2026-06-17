import XCTest
@testable import Ansight
@testable import AnsightCore

final class ArtifactToolTests: XCTestCase {
    override func tearDown() {
        AnsightRuntime.shared.clearArtifactProviders()
        AnsightRuntime.shared.closeSession()
        super.tearDown()
    }

    func testQueryArtifactsToolReturnsProviderAndDefinitions() throws {
        let result = try AnsightArtifactToolSupport.executeQuery(
            arguments: [
                AnsightToolExecutionArgumentNames.requestId: "req_1",
                AnsightToolExecutionArgumentNames.sessionId: "sess_1",
            ],
            providers: { [TestArtifactProvider()] }
        )

        XCTAssertTrue(result.success)
        guard case .object(let payload)? = result.result,
              case .array(let providers)? = payload["providers"],
              case .array(let artifacts)? = payload["artifacts"],
              case .integer(let providerCount)? = payload["providerCount"],
              case .integer(let artifactCount)? = payload["artifactCount"],
              case .object(let provider)? = providers.first,
              case .object(let artifact)? = artifacts.first,
              case .string(let providerId)? = provider["id"],
              case .string(let artifactProviderId)? = artifact["providerId"],
              case .string(let artifactId)? = artifact["id"] else {
            return XCTFail("Expected artifact query payload.")
        }

        XCTAssertEqual(providerCount, 1)
        XCTAssertEqual(artifactCount, 1)
        XCTAssertEqual(providerId, "app.report")
        XCTAssertEqual(artifactProviderId, "app.report")
        XCTAssertEqual(artifactId, "report.csv")
    }

    func testRequestArtifactToolRequiresLiveBinaryTransfer() throws {
        let runtime = AnsightRuntime.shared
        try runtime.initialize(options: AnsightOptions(toolGuard: .fullAccess))
        let result = try AnsightArtifactToolSupport.executeRequest(
            arguments: [
                AnsightToolExecutionArgumentNames.requestId: "req_2",
                AnsightToolExecutionArgumentNames.sessionId: "sess_1",
                "providerId": "app.report",
                "artifactId": "report.csv",
            ],
            providers: { [TestArtifactProvider()] },
            runtime: runtime
        )

        XCTAssertFalse(result.success)
        XCTAssertEqual(result.errorCode, "artifact_transfer_unavailable")
    }

    func testRegisterArtifactProviderInstallsCoreArtifactTools() throws {
        let runtime = AnsightRuntime.shared
        try runtime.initialize(options: AnsightOptions(toolGuard: .fullAccess))

        try runtime.registerArtifactProvider(TestArtifactProvider())

        XCTAssertEqual(runtime.registeredArtifactProviderIds(), ["app.report"])
        XCTAssertTrue(runtime.isToolRegistered(AnsightArtifactToolIds.query))
        XCTAssertTrue(runtime.isToolRegistered(AnsightArtifactToolIds.request))
    }

    func testRemoteToolOptionsIncludeArtifactTools() {
        let options = AnsightRemoteToolOptions(artifactProviders: [TestArtifactProvider()])

        let tools = AnsightRemoteTools.tools(options: options, runtime: .shared)

        XCTAssertTrue(tools.contains { $0.descriptor.id == AnsightArtifactToolIds.query })
        XCTAssertTrue(tools.contains { $0.descriptor.id == AnsightArtifactToolIds.request })
    }

    private struct TestArtifactProvider: AnsightArtifactProvider {
        let descriptor = AnsightArtifactProviderDescriptor(
            id: "app.report",
            name: "Reports",
            description: "App report exports.",
            category: "diagnostics",
            tags: ["report"],
            metadata: ["source": "unit-test"]
        )

        func query(context: AnsightArtifactQueryContext) throws -> [AnsightArtifactDefinition] {
            [
                AnsightArtifactDefinition(
                    id: "report.csv",
                    name: "Report CSV",
                    description: "CSV report.",
                    kind: "report",
                    category: "diagnostics",
                    content: AnsightArtifactContentDescriptor(
                        supportedMimeTypes: ["text/csv"],
                        defaultMimeType: "text/csv",
                        suggestedFileName: "report.csv",
                        supportsText: true,
                        supportsBinary: true,
                        sizeKnownBeforeCreation: true,
                        estimatedSizeBytes: 14
                    ),
                    tags: ["report"],
                    metadata: ["format": "csv"]
                ),
            ]
        }

        func create(request: AnsightArtifactRequest) throws -> AnsightArtifactResult {
            AnsightArtifactResult(
                metadata: AnsightArtifactMetadata(
                    artifactId: request.artifactId,
                    providerId: request.providerId,
                    name: "Report CSV",
                    kind: "report",
                    mimeType: "text/csv",
                    fileName: "report.csv",
                    description: "CSV report.",
                    tags: ["report"],
                    metadata: ["format": "csv"]
                ),
                payload: .fromText("id,name\n1,Ada\n")
            )
        }
    }
}
