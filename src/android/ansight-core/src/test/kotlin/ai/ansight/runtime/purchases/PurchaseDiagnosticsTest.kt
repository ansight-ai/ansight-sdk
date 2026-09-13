package ai.ansight.runtime.purchases

import java.io.File
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import ai.ansight.runtime.ToolPolicy
import ai.ansight.runtime.ToolSchemaValidator

class PurchaseDiagnosticsTest {
    @Test fun environmentGuardCoversEveryEntryPoint() {
        val diagnostics = PurchaseDiagnostics.forTesting { false }
        assertFalse(diagnostics.isInteropAllowed)
        assertThrows(PurchaseEnvironmentException::class.java) { diagnostics.record(PurchaseObservation("item", "app", environment = "sandbox")) }
        assertThrows(PurchaseEnvironmentException::class.java) { diagnostics.recordProduct(PurchaseProduct("item", true)) }
        assertThrows(PurchaseEnvironmentException::class.java) { diagnostics.createTransactionReference("token") }
        for (action in listOf("record", "recordProduct", "reference", "register", "clear") + PurchaseDiagnostics.toolIds) {
            assertThrows(PurchaseEnvironmentException::class.java) { diagnostics.command(JSONObject().put("action", action).toString()) }
        }
        for (id in PurchaseDiagnostics.toolIds) assertThrows(PurchaseEnvironmentException::class.java) { diagnostics.execute(id) }
        val context = ai.ansight.runtime.AndroidToolExecutionContext(android.app.Application(), null, null, null, ai.ansight.runtime.AnsightOptions())
        for (tool in diagnostics.tools()) {
            assertFalse(tool.availability(context).available)
            assertEquals(PurchaseEnvironmentException.ERROR_CODE, tool.availability(context).reasonCode)
            val result = (tool as ai.ansight.runtime.JsonAndroidTool).executeJson(JSONObject(), context)
            assertEquals(PurchaseEnvironmentException.ERROR_CODE, result.errorCode)
        }
        diagnostics.clear()
    }

    @Test fun environmentRechecksAndRejectsProductionEvidence() {
        var allowed = true
        val diagnostics = PurchaseDiagnostics.forTesting { allowed }
        diagnostics.record(PurchaseObservation("item", "store", environment = "sandbox"))
        allowed = false
        assertThrows(PurchaseEnvironmentException::class.java) { diagnostics.execute("purchases.get_state") }
        allowed = true
        assertEquals(0, diagnostics.execute("purchases.get_state").getJSONArray("observations").length())
        assertThrows(PurchaseEnvironmentException::class.java) { diagnostics.record(PurchaseObservation("item", "store", environment = "production")) }
        assertFalse(PurchaseDiagnostics.forTesting { error("Cannot detect environment") }.isInteropAllowed)
        assertTrue(PurchaseEnvironment.isEmulator("ranchu"))
        assertTrue(PurchaseEnvironment.isEmulator("goldfish"))
        for (hardware in listOf(null, "", "unknown", "test-keys", "generic", "qcom")) assertFalse(PurchaseEnvironment.isEmulator(hardware))
    }

    @Test fun referencesAreOpaqueAndAccountScoped() {
        val diagnostics = PurchaseDiagnostics.forTesting { true }
        val reference = diagnostics.createTransactionReference("raw-purchase-token")
        assertEquals(64, reference.length)
        assertEquals(reference, diagnostics.createTransactionReference("raw-purchase-token"))
        assertNotEquals(reference, PurchaseDiagnostics.forTesting { true }.createTransactionReference("raw-purchase-token"))
        diagnostics.clear()
        assertNotEquals(reference, diagnostics.createTransactionReference("raw-purchase-token"))
    }
    @Test fun sharedValidationCases() {
        val fixture = JSONObject(File("../../../docs/contracts/purchases/validation-cases.json").readText())
        val cases = fixture.getJSONArray("cases")
        for (i in 0 until cases.length()) {
            val test = cases.getJSONObject(i)
            val diagnostics = PurchaseDiagnostics.forTesting { true }
            val rows = test.getJSONArray("observations")
            for (j in 0 until rows.length()) diagnostics.record(PurchaseObservation.fromJson(rows.getJSONObject(j)))
            val result = diagnostics.execute("purchases.validate", test.getJSONObject("arguments"), fixture.getLong("now"))
            assertTrue(ToolSchemaValidator.validate(PurchaseSchemas.result, result).isEmpty())
            assertEquals(test.getString("name") + ": " + result, test.getString("status"), result.getString("status"))
        }
    }
    @Test fun retentionAndPassiveReads() {
        val diagnostics = PurchaseDiagnostics.forTesting { true }
        for (i in 0 until 300) diagnostics.record(PurchaseObservation("item", "app", i.toLong()))
        val result = diagnostics.execute("purchases.get_events")
        assertEquals(256, result.getJSONArray("observations").length())
        assertEquals(44, result.getLong("droppedEvents"))
        assertTrue(result.getBoolean("cursorGap"))
        for (tool in diagnostics.tools()) assertEquals(ToolPolicy.Read, tool.definition.policy)
        val before = diagnostics.execute("purchases.get_state", now = 300).toString()
        diagnostics.execute("purchases.validate", JSONObject().put("productId", "item").put("expectedEntitled", true), 300)
        assertEquals(before, diagnostics.execute("purchases.get_state", now = 300).toString())
    }
}
