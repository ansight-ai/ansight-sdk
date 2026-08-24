package ai.ansight.harness

import ai.ansight.Ansight
import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.AndroidUiEvidence
import ai.ansight.runtime.AnsightEventType
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightRuntime
import ai.ansight.runtime.AnsightSessionJpegCaptureOptions
import ai.ansight.runtime.FunctionAndroidTool
import ai.ansight.runtime.HostConnectionResult
import ai.ansight.runtime.ToolDefinition
import ai.ansight.runtime.ToolPolicy
import ai.ansight.runtime.ToolSchema
import ai.ansight.tools.reflection.AndroidReflectionRootRegistry
import android.app.AlertDialog
import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import android.graphics.Color
import android.graphics.Typeface
import android.opengl.GLES20
import android.opengl.GLSurfaceView
import android.opengl.Matrix
import android.os.Bundle
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import com.google.android.material.bottomsheet.BottomSheetDialog
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.FloatBuffer
import java.util.Locale
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.opengles.GL10

class MainActivity : AppCompatActivity() {
    private lateinit var sceneView: GLSurfaceView
    private lateinit var statusView: TextView
    private lateinit var contentHost: LinearLayout
    private lateinit var flyoutPanel: LinearLayout
    private lateinit var database: HarnessDatabase

    private val tabButtons = linkedMapOf<HarnessTab, Button>()
    private val harnessState = HarnessState()
    private var lastConnectionResult: HostConnectionResult? = null
    private var lastToolResult: JSONObject? = null
    private var lastDatabaseSummary: JSONObject? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        database = HarnessDatabase(this).also { it.seedIfNeeded() }
        lastDatabaseSummary = database.summary()
        registerReflectionRoots()

        setContentView(buildHarnessView())
        renderAll()

        runCatching {
            Ansight.initializeAndActivate(
                application = application,
                options = harnessOptions(),
            )
            AnsightRuntime.screenViewed("Harness.${harnessState.selectedTab.name.lowercase(Locale.US)}")
        }.onFailure { error ->
            harnessState.lastError = error.message ?: error.javaClass.simpleName
        }

        renderAll()

        if (intent.getBooleanExtra(EXTRA_CAPTURE_VALIDATION, false)) {
            statusView.postDelayed({
                showCaptureDialog()
                statusView.postDelayed({ captureEvidencePng() }, 800)
            }, 800)
        }
    }

    override fun onResume() {
        super.onResume()
        if (::sceneView.isInitialized) {
            sceneView.onResume()
        }
    }

    override fun onPause() {
        if (::sceneView.isInitialized) {
            sceneView.onPause()
        }
        super.onPause()
    }

    private fun harnessOptions(): AnsightOptions {
        val baseOptions = Ansight.developerOptions(
            clientName = "Ansight Android Harness",
        )
        return baseOptions.copy(
            enableBatteryLevel = true,
            sessionJpegCapture = AnsightSessionJpegCaptureOptions(
                intervalMilliseconds = 500,
                quality = 70,
                maxWidth = 720,
            ),
            hostAutoProbe = baseOptions.hostAutoProbe.copy(
                enabled = true,
                initialDelayMilliseconds = 1_000,
                probeIntervalMilliseconds = 5_000,
                reconnectDelayMilliseconds = 10_000,
                clientName = "Ansight Android Harness",
            ),
            customProperties = baseOptions.customProperties + mapOf(
                "harness" to mapOf(
                    "platform" to "android",
                    "screen" to harnessState.selectedTab.title,
                    "route" to (harnessState.navigationStack.lastOrNull()?.name ?: "Dashboard"),
                ),
            ),
            initialTools = baseOptions.initialTools + createHarnessTools(),
        )
    }

    private fun registerReflectionRoots() {
        AndroidReflectionRootRegistry.clear()
        AndroidReflectionRootRegistry.register(
            id = "harness.state",
            value = harnessState,
            displayName = "Harness State",
            description = "Mutable navigation, scene, modal, and tool state from the native Android harness.",
        )
        AndroidReflectionRootRegistry.registerGetter(
            id = "harness.database",
            displayName = "Harness Database",
            description = "SQLite helper and live database summary for the harness.",
        ) {
            database
        }
        AndroidReflectionRootRegistry.registerGetter(
            id = "harness.activity",
            displayName = "Harness Activity",
            description = "The currently running MainActivity instance.",
        ) {
            this
        }
    }

    private fun buildHarnessView(): View {
        val root = FrameLayout(this)
        val main = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.rgb(246, 247, 249))
        }

        main.addView(buildToolbar())

        statusView = TextView(this).apply {
            setTextColor(Color.rgb(31, 41, 55))
            setBackgroundColor(Color.rgb(235, 239, 245))
            setPadding(dp(12), dp(10), dp(12), dp(10))
            textSize = 13f
            typeface = Typeface.MONOSPACE
        }
        main.addView(statusView, linearParams(height = ViewGroup.LayoutParams.WRAP_CONTENT))

        sceneView = GLSurfaceView(this).apply {
            setEGLContextClientVersion(2)
            setRenderer(RotatingCubeRenderer(harnessState.scene))
            renderMode = GLSurfaceView.RENDERMODE_CONTINUOUSLY
        }
        main.addView(sceneView, linearParams(height = dp(240)))

        main.addView(buildTabBar())

        val scrollView = ScrollView(this).apply {
            isFillViewport = false
        }
        contentHost = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(14), dp(16), dp(24))
        }
        scrollView.addView(contentHost, ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
        ))
        main.addView(scrollView, linearParams(height = 0, weight = 1f))

        root.addView(main, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT,
        ))

        flyoutPanel = buildFlyoutPanel()
        root.addView(flyoutPanel, FrameLayout.LayoutParams(
            dp(292),
            ViewGroup.LayoutParams.MATCH_PARENT,
        ).apply {
            gravity = Gravity.START
        })
        flyoutPanel.visibility = View.GONE

        return root
    }

    private fun buildToolbar(): View {
        val toolbar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setBackgroundColor(Color.rgb(30, 41, 59))
            setPadding(dp(12), dp(10), dp(12), dp(10))
        }

        toolbar.addView(makeButton("Menu") {
            toggleFlyout()
        }, linearParams(width = dp(84), height = dp(42)))

        val title = TextView(this).apply {
            text = "Ansight Android Harness"
            setTextColor(Color.WHITE)
            textSize = 20f
            typeface = Typeface.DEFAULT_BOLD
            setPadding(dp(12), 0, 0, 0)
        }
        toolbar.addView(title, linearParams(width = 0, height = ViewGroup.LayoutParams.WRAP_CONTENT, weight = 1f))

        toolbar.addView(makeButton("Pair") {
            showPairingSheet()
        }, linearParams(width = dp(84), height = dp(42)))

        return toolbar
    }

    private fun buildTabBar(): View {
        val tabBar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setBackgroundColor(Color.rgb(226, 232, 240))
            setPadding(dp(8), dp(8), dp(8), dp(8))
        }

        HarnessTab.values().forEach { tab ->
            val button = makeButton(tab.title) {
                selectTab(tab)
            }
            tabButtons[tab] = button
            tabBar.addView(button, linearParams(width = 0, height = dp(44), weight = 1f).apply {
                marginEnd = dp(6)
            })
        }

        return tabBar
    }

    private fun buildFlyoutPanel(): LinearLayout {
        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.WHITE)
            elevation = dp(12).toFloat()
            setPadding(dp(16), dp(22), dp(16), dp(16))

            addSectionTitle(this, "Flyout Menu")
            addBodyText(this, "Switch routes from the side menu and verify drawer geometry, focus, and visual tree updates.")
            addSpacer(this, 12)

            addView(makeButton("Dashboard route") {
                pushRoute("Dashboard", "Opened from flyout")
                hideFlyout()
            })
            addView(makeButton("Orders route") {
                pushRoute("Orders", "Flyout selected the seeded SQLite order list")
                hideFlyout()
            })
            addView(makeButton("Profile route") {
                pushRoute("Profile", "Flyout selected a profile route")
                hideFlyout()
            })
            addView(makeButton("Settings route") {
                pushRoute("Settings", "Flyout selected settings")
                hideFlyout()
            })
            addView(makeButton("Close flyout") {
                hideFlyout()
            })
        }
    }

    private fun renderAll() {
        renderStatus()
        renderSelectedTab()
        renderTabButtons()
    }

    private fun renderStatus() {
        val snapshot = runCatching { AnsightRuntime.snapshot() }.getOrNull()
        val connectionResult = lastConnectionResult
        statusView.text = buildString {
            append("tab=${harnessState.selectedTab.title}")
            append(" route=${harnessState.navigationStack.lastOrNull()?.name ?: "<none>"}")
            append(" stack=${harnessState.navigationStack.size}")
            append(" modals=${harnessState.modalPresentations}")
            append(" dbItems=${lastDatabaseSummary?.optInt("itemCount") ?: 0}")
            append('\n')
            append("runtime=${snapshot?.connectionStatus?.connectionState ?: "<not initialized>"}")
            append(" sessionOpen=${snapshot?.sessionOpen ?: false}")
            append(" tools=${snapshot?.registeredTools ?: 0}")
            append(" metrics=${snapshot?.metricsRecorded ?: 0}")
            append(" events=${snapshot?.eventsRecorded ?: 0}")
            append(" touches=${snapshot?.touchesRecorded ?: 0}")
            if (connectionResult != null) {
                append('\n')
                append("lastConnect=${connectionResult.success}")
                append(" reason=${connectionResult.reasonCode ?: "<none>"}")
                append(" message=${connectionResult.message}")
            }
            if (!harnessState.lastError.isNullOrBlank()) {
                append('\n')
                append("lastError=${harnessState.lastError}")
            }
        }
    }

    private fun renderTabButtons() {
        tabButtons.forEach { (tab, button) ->
            val selected = tab == harnessState.selectedTab
            button.setTextColor(if (selected) Color.WHITE else Color.rgb(30, 41, 59))
            button.setBackgroundColor(if (selected) Color.rgb(37, 99, 235) else Color.WHITE)
        }
    }

    private fun renderSelectedTab() {
        contentHost.removeAllViews()
        when (harnessState.selectedTab) {
            HarnessTab.Overview -> renderOverview()
            HarnessTab.Navigation -> renderNavigation()
            HarnessTab.Data -> renderData()
            HarnessTab.Tools -> renderTools()
        }
    }

    private fun renderOverview() {
        addSectionTitle(contentHost, "Runtime Controls")
        addBodyText(contentHost, "This panel keeps the original runtime controls together with the larger inline OpenGL scene.")
        addButtonRow(
            contentHost,
            "Initialize" to { initializeRuntime() },
            "Activate" to {
                runCatching { AnsightRuntime.activate() }
                renderAll()
            },
        )
        addButtonRow(
            contentHost,
            "Metric" to {
                recordHarnessMetric()
                renderAll()
            },
            "Event" to {
                recordHarnessEvent("overview.event")
                renderAll()
            },
        )
        addButtonRow(
            contentHost,
            "Open Session" to {
                openHarnessSession()
                renderAll()
            },
            "QR Sheet" to {
                showPairingSheet()
            },
        )
        addButtonRow(
            contentHost,
            "Clear Buffers" to {
                AnsightRuntime.clear()
                renderAll()
            },
            "Capture PNG" to {
                captureEvidencePng()
            },
        )

        addSpacer(contentHost, 14)
        addSectionTitle(contentHost, "3D Viewer")
        addBodyText(
            contentHost,
            "The inline viewer uses a real GLSurfaceView with depth-tested rotating cube geometry for GPU screenshot validation.",
        )
        addButtonRow(
            contentHost,
            "Slow Cube" to {
                harnessState.scene.rotationSpeed = 22f
                renderAll()
            },
            "Fast Cube" to {
                harnessState.scene.rotationSpeed = 92f
                renderAll()
            },
        )
        addButtonRow(
            contentHost,
            "Swap Palette" to {
                harnessState.scene.paletteName = if (harnessState.scene.paletteName == "studio") "thermal" else "studio"
                recordHarnessEvent("scene.palette")
                renderAll()
            },
            "Modal" to {
                showCaptureDialog()
            },
        )
    }

    private fun renderNavigation() {
        addSectionTitle(contentHost, "Navigation Paradigms")
        addBodyText(contentHost, "Tabs, flyout, simulated push/pop stack, modal dialog, and bottom sheet are all active in this single harness.")
        addButtonRow(
            contentHost,
            "Push Details" to {
                pushRoute("Details", "Pushed from navigation controls")
            },
            "Push Checkout" to {
                pushRoute("Checkout", "Nested push route")
            },
        )
        addButtonRow(
            contentHost,
            "Pop" to {
                popRoute()
            },
            "Replace Settings" to {
                replaceRoute("Settings", "Route replaced in place")
            },
        )
        addButtonRow(
            contentHost,
            "Flyout" to {
                toggleFlyout()
            },
            "Bottom Sheet" to {
                showNavigationBottomSheet()
            },
        )
        addButtonRow(
            contentHost,
            "Dialog Modal" to {
                showNavigationDialog()
            },
            "Tab Data" to {
                selectTab(HarnessTab.Data)
            },
        )

        addSpacer(contentHost, 14)
        addSectionTitle(contentHost, "Route Stack")
        harnessState.navigationStack.forEachIndexed { index, route ->
            addListItem(
                contentHost,
                "${index + 1}. ${route.name}",
                route.detail,
            )
        }
    }

    private fun renderData() {
        lastDatabaseSummary = database.summary()
        addSectionTitle(contentHost, "SQLite Data")
        addBodyText(contentHost, "The harness seeds a local SQLite database for data.list_databases, data.describe_schema, and data.query.")
        addButtonRow(
            contentHost,
            "Seed DB" to {
                database.seed()
                recordHarnessEvent("database.seed")
                renderAll()
            },
            "Add Order" to {
                val label = database.insertGeneratedItem()
                harnessState.data.lastInsertedItem = label
                recordHarnessEvent("database.insert")
                renderAll()
            },
        )
        addButtonRow(
            contentHost,
            "Query Summary" to {
                lastDatabaseSummary = database.summary()
                recordHarnessEvent("database.summary")
                renderAll()
            },
            "Push Data Route" to {
                pushRoute("Data Detail", "Route created from the database panel")
            },
        )

        addSpacer(contentHost, 14)
        addSectionTitle(contentHost, "Database Summary")
        val summary = lastDatabaseSummary ?: JSONObject()
        addListItem(contentHost, "Database", summary.optString("path", "<unknown>"))
        addListItem(contentHost, "Items", summary.optInt("itemCount").toString())
        addListItem(contentHost, "Events", summary.optInt("eventCount").toString())
        addListItem(contentHost, "Last inserted", harnessState.data.lastInsertedItem ?: "<none>")
    }

    private fun renderTools() {
        addSectionTitle(contentHost, "Reflection Roots")
        addBodyText(contentHost, "Registered roots: harness.state, harness.database, and harness.activity. reflect.list_roots reports hostRuntime.kind=jvm for these Android roots.")
        addListItem(contentHost, "harness.state", "Mutable navigation, data, scene, and custom tool state.")
        addListItem(contentHost, "harness.database", "SQLite helper with a live summary and database path.")
        addListItem(contentHost, "harness.activity", "Current MainActivity instance.")

        addSpacer(contentHost, 14)
        addSectionTitle(contentHost, "Custom Ansight Tools")
        addBodyText(contentHost, "These are registered beside the standard SDK tools for app-specific state inspection.")
        addListItem(contentHost, HarnessToolIds.InspectState, "Returns navigation, database, scene, and runtime state.")
        addListItem(contentHost, HarnessToolIds.AdvanceState, "Mutates state with push, pop, tab, seed, insert, and palette actions.")
        addListItem(contentHost, HarnessToolIds.DatabaseSummary, "Returns a focused SQLite summary.")
        addButtonRow(
            contentHost,
            "Run Inspect" to {
                lastToolResult = inspectHarnessState()
                renderAll()
            },
            "Run Advance" to {
                advanceHarnessState("push")
                renderAll()
            },
        )

        addSpacer(contentHost, 14)
        addSectionTitle(contentHost, "Mutable Custom Properties")
        addBodyText(contentHost, "Updates are sent through session.properties when the harness is connected.")
        addButtonRow(
            contentHost,
            "Set Props" to {
                setHarnessCustomProperties()
            },
            "Clear Props" to {
                clearHarnessCustomProperties()
            },
        )

        addSpacer(contentHost, 14)
        addSectionTitle(contentHost, "Last Custom Tool Result")
        addBodyText(contentHost, lastToolResult?.toString(2) ?: "<none>")
    }

    private fun initializeRuntime() {
        runCatching {
            Ansight.initialize(
                application = application,
                options = harnessOptions(),
            )
        }.onFailure { error ->
            harnessState.lastError = error.message ?: error.javaClass.simpleName
        }
        renderAll()
    }

    private fun openHarnessSession() {
        lastConnectionResult = runCatching {
            AnsightRuntime.connect()
        }.getOrElse { ex ->
            HostConnectionResult.failure(ex.message ?: "Connection failed.")
        }
    }

    private fun showPairingSheet() {
        Ansight.showPairingSheet(
            activity = this,
            clientName = "Ansight Android Harness",
            expectedAppId = packageName,
            onResult = { result ->
                lastConnectionResult = result
                renderAll()
            },
            onError = { error ->
                lastConnectionResult = HostConnectionResult.failure(error.message ?: "Pairing sheet failed.")
                renderAll()
            },
        )
    }

    private fun showCaptureDialog() {
        harnessState.modalPresentations += 1
        AlertDialog.Builder(this)
            .setTitle("Ansight modal capture")
            .setMessage("This dialog should be visible in screenshots while the inline 3D viewer remains visible behind it.")
            .setPositiveButton("Capture") { _, _ -> captureEvidencePng() }
            .setNegativeButton("Close", null)
            .setOnDismissListener {
                harnessState.modalDismissals += 1
                renderAll()
            }
            .show()
        renderAll()
    }

    private fun showNavigationDialog() {
        harnessState.modalPresentations += 1
        AlertDialog.Builder(this)
            .setTitle("Push/pop modal")
            .setMessage("This modal increments state and can push a route before it closes.")
            .setPositiveButton("Push Route") { _, _ ->
                pushRoute("Modal Result", "Created from AlertDialog")
            }
            .setNegativeButton("Dismiss", null)
            .setOnDismissListener {
                harnessState.modalDismissals += 1
                renderAll()
            }
            .show()
        renderAll()
    }

    private fun showNavigationBottomSheet() {
        harnessState.modalPresentations += 1
        val dialog = BottomSheetDialog(this)
        val sheet = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(18), dp(18), dp(24))
            addSectionTitle(this, "Bottom Sheet Flow")
            addBodyText(this, "Use this to verify Material bottom sheet capture and modal visual tree placement.")
            addView(makeButton("Push sheet route") {
                pushRoute("Bottom Sheet", "Created from Material bottom sheet")
                dialog.dismiss()
            })
            addView(makeButton("Insert database row") {
                harnessState.data.lastInsertedItem = database.insertGeneratedItem()
                dialog.dismiss()
                renderAll()
            })
            addView(makeButton("Dismiss") {
                dialog.dismiss()
            })
        }
        dialog.setContentView(sheet)
        dialog.setOnDismissListener {
            harnessState.modalDismissals += 1
            renderAll()
        }
        dialog.show()
        renderAll()
    }

    private fun captureEvidencePng() {
        val result = runCatching {
            val screenshot = AndroidUiEvidence.captureScreenshot(format = "png", quality = 100, maxWidth = 720)
            val output = File(filesDir, "ansight-capture-validation.png")
            output.writeBytes(screenshot.bytes)
            "capture=${output.absolutePath} bytes=${screenshot.bytes.size} size=${screenshot.width}x${screenshot.height}"
        }.getOrElse { error ->
            "captureError=${error.message ?: error.javaClass.simpleName}"
        }
        harnessState.lastCapture = result
        renderAll()
    }

    private fun recordHarnessMetric() {
        val value = System.currentTimeMillis() % 10_000
        harnessState.metricButtonTaps += 1
        AnsightRuntime.metric(value = value, channel = 42)
    }

    private fun recordHarnessEvent(label: String) {
        harnessState.eventButtonTaps += 1
        AnsightRuntime.event(
            label = label,
            type = AnsightEventType.Navigation,
            details = "tab=${harnessState.selectedTab.title};route=${harnessState.navigationStack.lastOrNull()?.name}",
            channel = 42,
        )
    }

    private fun selectTab(tab: HarnessTab) {
        harnessState.selectedTab = tab
        AnsightRuntime.screenViewed("Harness.${tab.name.lowercase(Locale.US)}")
        renderAll()
    }

    private fun pushRoute(name: String, detail: String) {
        harnessState.navigationStack.add(HarnessRoute(name, detail))
        harnessState.navigationOperations += 1
        recordHarnessEvent("navigation.push")
        renderAll()
    }

    private fun popRoute() {
        if (harnessState.navigationStack.size > 1) {
            harnessState.navigationStack.removeAt(harnessState.navigationStack.lastIndex)
        }
        harnessState.navigationOperations += 1
        recordHarnessEvent("navigation.pop")
        renderAll()
    }

    private fun replaceRoute(name: String, detail: String) {
        if (harnessState.navigationStack.isEmpty()) {
            harnessState.navigationStack.add(HarnessRoute(name, detail))
        } else {
            harnessState.navigationStack[harnessState.navigationStack.lastIndex] = HarnessRoute(name, detail)
        }
        harnessState.navigationOperations += 1
        recordHarnessEvent("navigation.replace")
        renderAll()
    }

    private fun toggleFlyout() {
        if (flyoutPanel.visibility == View.VISIBLE) {
            hideFlyout()
        } else {
            harnessState.flyoutOpens += 1
            flyoutPanel.visibility = View.VISIBLE
            renderAll()
        }
    }

    private fun hideFlyout() {
        flyoutPanel.visibility = View.GONE
        renderAll()
    }

    private fun inspectHarnessState(): JSONObject {
        val payload = harnessState.toJson(database.summary())
        lastToolResult = payload
        return payload
    }

    private fun advanceHarnessState(action: String): JSONObject {
        when (action.trim().lowercase(Locale.US)) {
            "push" -> harnessState.navigationStack.add(HarnessRoute("Tool Route", "Pushed by custom Ansight tool"))
            "pop" -> if (harnessState.navigationStack.size > 1) {
                harnessState.navigationStack.removeAt(harnessState.navigationStack.lastIndex)
            }
            "tab_overview" -> harnessState.selectedTab = HarnessTab.Overview
            "tab_navigation" -> harnessState.selectedTab = HarnessTab.Navigation
            "tab_data" -> harnessState.selectedTab = HarnessTab.Data
            "tab_tools" -> harnessState.selectedTab = HarnessTab.Tools
            "seed_database" -> database.seed()
            "insert_item" -> harnessState.data.lastInsertedItem = database.insertGeneratedItem()
            "palette" -> harnessState.scene.paletteName = if (harnessState.scene.paletteName == "studio") "thermal" else "studio"
            "modal" -> harnessState.modalPresentations += 1
        }
        harnessState.customToolInvocations += 1
        lastToolResult = harnessState.toJson(database.summary())
        runOnUiThread { renderAll() }
        return lastToolResult ?: JSONObject()
    }

    private fun setHarnessCustomProperties() {
        val route = harnessState.navigationStack.lastOrNull()?.name ?: "Dashboard"
        val result = Ansight.updateCustomProperties(
            mapOf(
                "harness" to mapOf(
                    "screen" to harnessState.selectedTab.title,
                    "route" to route,
                    "stackDepth" to harnessState.navigationStack.size.toString(),
                    "modalPresentations" to harnessState.modalPresentations.toString(),
                ),
                "scene" to mapOf(
                    "palette" to harnessState.scene.paletteName,
                    "rotationSpeed" to harnessState.scene.rotationSpeed.toString(),
                ),
            ),
        )
        lastToolResult = JSONObject()
            .put("customPropertiesUpdated", result.success)
            .put("message", result.message)
        renderAll()
    }

    private fun clearHarnessCustomProperties() {
        val result = Ansight.clearCustomProperties()
        lastToolResult = JSONObject()
            .put("customPropertiesCleared", result.success)
            .put("message", result.message)
        renderAll()
    }

    private fun createHarnessTools(): List<AndroidTool> = listOf(
        FunctionAndroidTool(
            ToolDefinition(
                id = HarnessToolIds.InspectState,
                name = "Inspect Harness State",
                description = "Returns app-specific harness navigation, database, scene, and runtime state.",
                category = "harness",
                policy = ToolPolicy.Read,
                keywords = "harness android state navigation database scene",
                argumentsSchema = ToolSchema.obj(description = "No arguments."),
                resultSchema = ToolSchema.obj(additionalProperties = true),
            ),
        ) { _, _ ->
            AndroidToolResult.success(inspectHarnessState())
        },
        FunctionAndroidTool(
            ToolDefinition(
                id = HarnessToolIds.AdvanceState,
                name = "Advance Harness State",
                description = "Mutates the harness state using a named action.",
                category = "harness",
                policy = ToolPolicy.Write,
                keywords = "harness android mutate navigation database tab",
                argumentsSchema = ToolSchema.obj(
                    description = "Harness state mutation arguments.",
                    properties = mapOf(
                        "action" to ToolSchema.string(
                            description = "Action to run.",
                            enumValues = listOf(
                                "push",
                                "pop",
                                "tab_overview",
                                "tab_navigation",
                                "tab_data",
                                "tab_tools",
                                "seed_database",
                                "insert_item",
                                "palette",
                                "modal",
                            ),
                        ),
                    ),
                    required = listOf("action"),
                ),
                resultSchema = ToolSchema.obj(additionalProperties = true),
            ),
        ) { args, _ ->
            AndroidToolResult.success(advanceHarnessState(args["action"] ?: "push"))
        },
        FunctionAndroidTool(
            ToolDefinition(
                id = HarnessToolIds.DatabaseSummary,
                name = "Harness Database Summary",
                description = "Returns a focused summary of the seeded harness SQLite database.",
                category = "harness",
                policy = ToolPolicy.Read,
                keywords = "harness android sqlite database summary",
                argumentsSchema = ToolSchema.obj(description = "No arguments."),
                resultSchema = ToolSchema.obj(additionalProperties = true),
            ),
        ) { _, _ ->
            AndroidToolResult.success(database.summary())
        },
    )

    private fun addButtonRow(parent: LinearLayout, first: Pair<String, () -> Unit>, second: Pair<String, () -> Unit>) {
        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        row.addView(makeButton(first.first, first.second), linearParams(width = 0, height = dp(46), weight = 1f).apply {
            marginEnd = dp(8)
        })
        row.addView(makeButton(second.first, second.second), linearParams(width = 0, height = dp(46), weight = 1f))
        parent.addView(row, linearParams(height = ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(8)
        })
    }

    private fun makeButton(label: String, action: () -> Unit): Button = Button(this).apply {
        text = label
        isAllCaps = false
        textSize = 13f
        minHeight = 0
        setOnClickListener { action() }
    }

    private fun addSectionTitle(parent: LinearLayout, text: String) {
        parent.addView(TextView(this).apply {
            this.text = text
            textSize = 18f
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(Color.rgb(15, 23, 42))
        }, linearParams(height = ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(4)
            bottomMargin = dp(6)
        })
    }

    private fun addBodyText(parent: LinearLayout, text: String) {
        parent.addView(TextView(this).apply {
            this.text = text
            textSize = 14f
            setTextColor(Color.rgb(71, 85, 105))
            setLineSpacing(0f, 1.1f)
        }, linearParams(height = ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(6)
        })
    }

    private fun addListItem(parent: LinearLayout, title: String, detail: String) {
        val item = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.WHITE)
            setPadding(dp(12), dp(10), dp(12), dp(10))
        }
        item.addView(TextView(this).apply {
            text = title
            textSize = 15f
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(Color.rgb(30, 41, 59))
        })
        item.addView(TextView(this).apply {
            text = detail
            textSize = 13f
            setTextColor(Color.rgb(71, 85, 105))
        })
        parent.addView(item, linearParams(height = ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(8)
        })
    }

    private fun addSpacer(parent: LinearLayout, heightDp: Int) {
        parent.addView(View(this), linearParams(height = dp(heightDp)))
    }

    private fun linearParams(
        width: Int = ViewGroup.LayoutParams.MATCH_PARENT,
        height: Int,
        weight: Float = 0f,
    ): LinearLayout.LayoutParams = LinearLayout.LayoutParams(width, height, weight)

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private enum class HarnessTab(val title: String) {
        Overview("Overview"),
        Navigation("Navigation"),
        Data("Data"),
        Tools("Tools"),
    }

    private data class HarnessRoute(
        var name: String,
        var detail: String,
    )

    private data class HarnessSceneState(
        @Volatile var rotationSpeed: Float = 46f,
        @Volatile var paletteName: String = "studio",
        @Volatile var lastFrameEpochMs: Long = 0,
    ) {
        fun toJson(): JSONObject = JSONObject()
            .put("rotationSpeed", rotationSpeed)
            .put("paletteName", paletteName)
            .put("lastFrameEpochMs", lastFrameEpochMs)
    }

    private data class HarnessDataState(
        var lastInsertedItem: String? = null,
    ) {
        fun toJson(): JSONObject = JSONObject()
            .put("lastInsertedItem", lastInsertedItem ?: JSONObject.NULL)
    }

    private data class HarnessState(
        var selectedTab: HarnessTab = HarnessTab.Overview,
        val navigationStack: MutableList<HarnessRoute> = mutableListOf(
            HarnessRoute("Dashboard", "Initial root route"),
        ),
        val scene: HarnessSceneState = HarnessSceneState(),
        val data: HarnessDataState = HarnessDataState(),
        var metricButtonTaps: Int = 0,
        var eventButtonTaps: Int = 0,
        var navigationOperations: Int = 0,
        var modalPresentations: Int = 0,
        var modalDismissals: Int = 0,
        var flyoutOpens: Int = 0,
        var customToolInvocations: Int = 0,
        var lastCapture: String? = null,
        var lastError: String? = null,
    ) {
        fun toJson(databaseSummary: JSONObject): JSONObject = JSONObject()
            .put("selectedTab", selectedTab.title)
            .put("navigationStack", JSONArray(navigationStack.map { route ->
                JSONObject()
                    .put("name", route.name)
                    .put("detail", route.detail)
            }))
            .put("scene", scene.toJson())
            .put("data", data.toJson())
            .put("metricButtonTaps", metricButtonTaps)
            .put("eventButtonTaps", eventButtonTaps)
            .put("navigationOperations", navigationOperations)
            .put("modalPresentations", modalPresentations)
            .put("modalDismissals", modalDismissals)
            .put("flyoutOpens", flyoutOpens)
            .put("customToolInvocations", customToolInvocations)
            .put("lastCapture", lastCapture ?: JSONObject.NULL)
            .put("lastError", lastError ?: JSONObject.NULL)
            .put("database", databaseSummary)
    }

    private class HarnessDatabase(context: Context) : SQLiteOpenHelper(context, DatabaseName, null, DatabaseVersion) {
        override fun onCreate(db: SQLiteDatabase) {
            createSchema(db)
        }

        override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
            db.execSQL("DROP TABLE IF EXISTS harness_events")
            db.execSQL("DROP TABLE IF EXISTS harness_items")
            onCreate(db)
        }

        fun seedIfNeeded() {
            if (count("harness_items") == 0) {
                seed()
            }
        }

        fun seed() {
            writableDatabase.use { db ->
                createSchema(db)
                db.delete("harness_events", null, null)
                db.delete("harness_items", null, null)
                listOf("Alpha order", "Beta invoice", "Gamma session", "Delta profile").forEachIndexed { index, label ->
                    db.insert(
                        "harness_items",
                        null,
                        ContentValues().apply {
                            put("label", label)
                            put("kind", if (index % 2 == 0) "order" else "profile")
                            put("quantity", index + 1)
                            put("created_at", System.currentTimeMillis() - index * 60_000L)
                        },
                    )
                }
                db.insert(
                    "harness_events",
                    null,
                    ContentValues().apply {
                        put("label", "database.seed")
                        put("severity", "info")
                        put("created_at", System.currentTimeMillis())
                    },
                )
            }
        }

        fun insertGeneratedItem(): String {
            val label = "Generated item ${System.currentTimeMillis() % 100_000}"
            writableDatabase.use { db ->
                db.insert(
                    "harness_items",
                    null,
                    ContentValues().apply {
                        put("label", label)
                        put("kind", "generated")
                        put("quantity", (System.currentTimeMillis() % 7 + 1).toInt())
                        put("created_at", System.currentTimeMillis())
                    },
                )
                db.insert(
                    "harness_events",
                    null,
                    ContentValues().apply {
                        put("label", "database.insert")
                        put("severity", "info")
                        put("created_at", System.currentTimeMillis())
                    },
                )
            }
            return label
        }

        fun summary(): JSONObject {
            return JSONObject()
                .put("name", DatabaseName)
                .put("path", readableDatabase.path)
                .put("itemCount", count("harness_items"))
                .put("eventCount", count("harness_events"))
                .put("latestItem", latestItem() ?: JSONObject.NULL)
        }

        private fun createSchema(db: SQLiteDatabase) {
            db.execSQL(
                """
                CREATE TABLE IF NOT EXISTS harness_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    label TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    quantity INTEGER NOT NULL,
                    created_at INTEGER NOT NULL
                )
                """.trimIndent(),
            )
            db.execSQL(
                """
                CREATE TABLE IF NOT EXISTS harness_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    label TEXT NOT NULL,
                    severity TEXT NOT NULL,
                    created_at INTEGER NOT NULL
                )
                """.trimIndent(),
            )
        }

        private fun count(table: String): Int {
            readableDatabase.rawQuery("SELECT COUNT(*) FROM $table", emptyArray()).use { cursor ->
                return if (cursor.moveToFirst()) cursor.getInt(0) else 0
            }
        }

        private fun latestItem(): String? {
            readableDatabase.rawQuery(
                "SELECT label FROM harness_items ORDER BY id DESC LIMIT 1",
                emptyArray(),
            ).use { cursor ->
                return if (cursor.moveToFirst()) cursor.getString(0) else null
            }
        }

        private companion object {
            const val DatabaseName = "ansight_harness.db"
            const val DatabaseVersion = 1
        }
    }

    private class RotatingCubeRenderer(
        private val sceneState: HarnessSceneState,
    ) : GLSurfaceView.Renderer {
        private val vertexBuffer: FloatBuffer = makeFloatBuffer(CubeVertices)
        private val colorBuffer: FloatBuffer = makeFloatBuffer(CubeColors)
        private val projectionMatrix = FloatArray(16)
        private val viewMatrix = FloatArray(16)
        private val modelMatrix = FloatArray(16)
        private val mvpMatrix = FloatArray(16)
        private val scratchMatrix = FloatArray(16)
        private var program = 0
        private var positionHandle = 0
        private var colorHandle = 0
        private var matrixHandle = 0
        private val startedAtNanos = System.nanoTime()

        override fun onSurfaceCreated(gl: GL10?, config: EGLConfig?) {
            GLES20.glEnable(GLES20.GL_DEPTH_TEST)
            GLES20.glClearColor(0.04f, 0.06f, 0.09f, 1f)
            program = GLES20.glCreateProgram()
            GLES20.glAttachShader(program, compileShader(GLES20.GL_VERTEX_SHADER, VertexShader))
            GLES20.glAttachShader(program, compileShader(GLES20.GL_FRAGMENT_SHADER, FragmentShader))
            GLES20.glLinkProgram(program)
            positionHandle = GLES20.glGetAttribLocation(program, "aPosition")
            colorHandle = GLES20.glGetAttribLocation(program, "aColor")
            matrixHandle = GLES20.glGetUniformLocation(program, "uMvpMatrix")
        }

        override fun onSurfaceChanged(gl: GL10?, width: Int, height: Int) {
            GLES20.glViewport(0, 0, width, height)
            val ratio = width.toFloat() / height.toFloat().coerceAtLeast(1f)
            Matrix.perspectiveM(projectionMatrix, 0, 45f, ratio, 1f, 100f)
            Matrix.setLookAtM(viewMatrix, 0, 0f, 0f, 5.6f, 0f, 0f, 0f, 0f, 1f, 0f)
        }

        override fun onDrawFrame(gl: GL10?) {
            sceneState.lastFrameEpochMs = System.currentTimeMillis()
            GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT or GLES20.GL_DEPTH_BUFFER_BIT)
            GLES20.glUseProgram(program)

            val elapsed = (System.nanoTime() - startedAtNanos) / 1_000_000_000f
            Matrix.setIdentityM(modelMatrix, 0)
            Matrix.rotateM(modelMatrix, 0, elapsed * sceneState.rotationSpeed, 0.7f, 1f, 0.35f)
            Matrix.multiplyMM(scratchMatrix, 0, viewMatrix, 0, modelMatrix, 0)
            Matrix.multiplyMM(mvpMatrix, 0, projectionMatrix, 0, scratchMatrix, 0)

            vertexBuffer.position(0)
            colorBuffer.position(0)
            GLES20.glEnableVertexAttribArray(positionHandle)
            GLES20.glEnableVertexAttribArray(colorHandle)
            GLES20.glVertexAttribPointer(positionHandle, 3, GLES20.GL_FLOAT, false, 0, vertexBuffer)
            GLES20.glVertexAttribPointer(colorHandle, 4, GLES20.GL_FLOAT, false, 0, colorBuffer)
            GLES20.glUniformMatrix4fv(matrixHandle, 1, false, mvpMatrix, 0)
            GLES20.glDrawArrays(GLES20.GL_TRIANGLES, 0, CubeVertices.size / 3)
            GLES20.glDisableVertexAttribArray(positionHandle)
            GLES20.glDisableVertexAttribArray(colorHandle)
        }

        private fun compileShader(type: Int, source: String): Int {
            val shader = GLES20.glCreateShader(type)
            GLES20.glShaderSource(shader, source)
            GLES20.glCompileShader(shader)
            return shader
        }

        private companion object {
            fun makeFloatBuffer(values: FloatArray): FloatBuffer = ByteBuffer
                .allocateDirect(values.size * 4)
                .order(ByteOrder.nativeOrder())
                .asFloatBuffer()
                .apply {
                    put(values)
                    position(0)
                }

            const val VertexShader = """
                uniform mat4 uMvpMatrix;
                attribute vec4 aPosition;
                attribute vec4 aColor;
                varying vec4 vColor;
                void main() {
                    vColor = aColor;
                    gl_Position = uMvpMatrix * aPosition;
                }
            """

            const val FragmentShader = """
                precision mediump float;
                varying vec4 vColor;
                void main() {
                    gl_FragColor = vColor;
                }
            """

            val CubeVertices = floatArrayOf(
                -1f, -1f, 1f, 1f, -1f, 1f, 1f, 1f, 1f,
                -1f, -1f, 1f, 1f, 1f, 1f, -1f, 1f, 1f,
                -1f, -1f, -1f, -1f, 1f, -1f, 1f, 1f, -1f,
                -1f, -1f, -1f, 1f, 1f, -1f, 1f, -1f, -1f,
                -1f, 1f, -1f, -1f, 1f, 1f, 1f, 1f, 1f,
                -1f, 1f, -1f, 1f, 1f, 1f, 1f, 1f, -1f,
                -1f, -1f, -1f, 1f, -1f, -1f, 1f, -1f, 1f,
                -1f, -1f, -1f, 1f, -1f, 1f, -1f, -1f, 1f,
                1f, -1f, -1f, 1f, 1f, -1f, 1f, 1f, 1f,
                1f, -1f, -1f, 1f, 1f, 1f, 1f, -1f, 1f,
                -1f, -1f, -1f, -1f, -1f, 1f, -1f, 1f, 1f,
                -1f, -1f, -1f, -1f, 1f, 1f, -1f, 1f, -1f,
            )

            val CubeColors = FloatArray(36 * 4).apply {
                val colors = arrayOf(
                    floatArrayOf(0.22f, 0.53f, 0.96f, 1f),
                    floatArrayOf(0.11f, 0.72f, 0.55f, 1f),
                    floatArrayOf(0.94f, 0.35f, 0.32f, 1f),
                    floatArrayOf(0.98f, 0.72f, 0.24f, 1f),
                    floatArrayOf(0.58f, 0.36f, 0.86f, 1f),
                    floatArrayOf(0.08f, 0.64f, 0.78f, 1f),
                )
                var offset = 0
                for (face in 0 until 6) {
                    repeat(6) {
                        colors[face].copyInto(this, offset)
                        offset += 4
                    }
                }
            }
        }
    }

    private object HarnessToolIds {
        const val InspectState = "harness.inspect_state"
        const val AdvanceState = "harness.advance_state"
        const val DatabaseSummary = "harness.database_summary"
    }

    private companion object {
        const val EXTRA_CAPTURE_VALIDATION = "ai.ansight.harness.CAPTURE_VALIDATION"
    }
}
