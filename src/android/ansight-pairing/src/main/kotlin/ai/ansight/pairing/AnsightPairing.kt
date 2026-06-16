package ai.ansight.pairing

import ai.ansight.runtime.AnsightRuntime
import ai.ansight.runtime.HostConnectionRequest
import ai.ansight.runtime.HostConnectionRequestKind
import ai.ansight.runtime.HostConnectionResult
import android.app.Activity
import android.os.Handler
import android.os.Looper
import android.text.InputType
import android.util.TypedValue
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView
import com.google.android.gms.tasks.OnCanceledListener
import com.google.android.gms.tasks.OnFailureListener
import com.google.android.gms.tasks.OnSuccessListener
import com.google.android.material.bottomsheet.BottomSheetDialog
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning

object AnsightPairing {
    fun scanQrCode(
        activity: Activity,
        onPayload: (String?) -> Unit,
        onError: (Throwable) -> Unit = {},
    ) {
        if (activity.isFinishing || activity.isDestroyed) {
            onError(IllegalStateException("QR pairing is unavailable because the current Android activity is no longer active."))
            return
        }

        val options = GmsBarcodeScannerOptions.Builder()
            .setBarcodeFormats(Barcode.FORMAT_QR_CODE)
            .enableAutoZoom()
            .build()
        val scanner = GmsBarcodeScanning.getClient(activity, options)
        activity.runOnUiThread {
            scanner.startScan()
                .addOnSuccessListener(QrSuccessListener(onPayload, onError))
                .addOnFailureListener(QrFailureListener(onError))
                .addOnCanceledListener(QrCanceledListener(onPayload))
        }
    }

    @JvmOverloads
    fun connectFromQrCode(
        activity: Activity,
        clientName: String? = null,
        expectedAppId: String? = activity.packageName,
        hostAddressOverride: String? = null,
        onResult: (HostConnectionResult) -> Unit = {},
        onError: (Throwable) -> Unit = {},
    ) {
        scanQrCode(
            activity = activity,
            onPayload = { payload ->
                if (payload.isNullOrBlank()) {
                    return@scanQrCode
                }
                connectFromPayload(
                    activity = activity,
                    payload = payload,
                    kind = HostConnectionRequestKind.QrCode,
                    clientName = clientName,
                    expectedAppId = expectedAppId,
                    hostAddressOverride = hostAddressOverride,
                    onResult = onResult,
                    onError = onError,
                )
            },
            onError = onError,
        )
    }

    @JvmOverloads
    fun showPairingSheet(
        activity: Activity,
        clientName: String? = null,
        expectedAppId: String? = activity.packageName,
        hostAddressOverride: String? = null,
        onResult: (HostConnectionResult) -> Unit = {},
        onError: (Throwable) -> Unit = {},
    ): BottomSheetDialog {
        val dialog = BottomSheetDialog(activity)
        val density = activity.resources.displayMetrics.density
        val horizontal = (24 * density).toInt()
        val vertical = (18 * density).toInt()
        val gap = (12 * density).toInt()

        val container = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(horizontal, vertical, horizontal, vertical)
            layoutParams = ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
        }
        val title = TextView(activity).apply {
            text = "Connect Ansight"
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f)
        }
        val status = TextView(activity).apply {
            text = "Scan a QR code or paste a pairing payload."
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
        }
        val payloadInput = EditText(activity).apply {
            hint = "Pairing payload"
            minLines = 4
            maxLines = 8
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE
        }
        val scanButton = Button(activity).apply {
            text = "Scan QR"
        }
        val connectButton = Button(activity).apply {
            text = "Connect"
        }
        val cancelButton = Button(activity).apply {
            text = "Cancel"
        }

        listOf(title, status, payloadInput, scanButton, connectButton, cancelButton).forEach { view ->
            container.addView(
                view,
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                    bottomMargin = gap
                },
            )
        }

        scanButton.setOnClickListener {
            scanButton.isEnabled = false
            status.text = "Opening scanner..."
            scanQrCode(
                activity = activity,
                onPayload = { payload ->
                    activity.runOnUiThread {
                        scanButton.isEnabled = true
                        if (payload.isNullOrBlank()) {
                            status.text = "Scan canceled."
                        } else {
                            payloadInput.setText(payload)
                            status.text = "Pairing code detected."
                        }
                    }
                },
                onError = { error ->
                    activity.runOnUiThread {
                        scanButton.isEnabled = true
                        status.text = error.message ?: "QR scan failed."
                    }
                    onError(error)
                },
            )
        }
        connectButton.setOnClickListener {
            val payload = payloadInput.text?.toString()?.trim().orEmpty()
            if (payload.isBlank()) {
                status.text = "Paste or scan a pairing payload."
                return@setOnClickListener
            }
            connectButton.isEnabled = false
            status.text = "Connecting..."
            connectFromPayload(
                activity = activity,
                payload = payload,
                kind = HostConnectionRequestKind.Payload,
                clientName = clientName,
                expectedAppId = expectedAppId,
                hostAddressOverride = hostAddressOverride,
                onResult = { result ->
                    activity.runOnUiThread {
                        connectButton.isEnabled = true
                        status.text = result.message
                        if (result.success) {
                            dialog.dismiss()
                        }
                    }
                    onResult(result)
                },
                onError = { error ->
                    activity.runOnUiThread {
                        connectButton.isEnabled = true
                        status.text = error.message ?: "Connection failed."
                    }
                    onError(error)
                },
            )
        }
        cancelButton.setOnClickListener {
            dialog.dismiss()
        }

        dialog.setContentView(container)
        dialog.show()
        return dialog
    }

    private fun connectFromPayload(
        activity: Activity,
        payload: String,
        kind: HostConnectionRequestKind,
        clientName: String?,
        expectedAppId: String?,
        hostAddressOverride: String?,
        onResult: (HostConnectionResult) -> Unit,
        onError: (Throwable) -> Unit,
    ) {
        Thread {
            try {
                val result = AnsightRuntime.connect(
                    HostConnectionRequest(
                        kind = kind,
                        payload = payload,
                        clientName = clientName,
                        expectedAppId = expectedAppId,
                        hostAddressOverride = hostAddressOverride,
                    ),
                )
                Handler(Looper.getMainLooper()).post { onResult(result) }
            } catch (ex: Throwable) {
                Handler(Looper.getMainLooper()).post { onError(ex) }
            }
        }.apply {
            name = "AnsightAndroidPairingConnect"
            isDaemon = true
            start()
        }
    }

    private class QrSuccessListener(
        private val onPayload: (String?) -> Unit,
        private val onError: (Throwable) -> Unit,
    ) : OnSuccessListener<Barcode> {
        override fun onSuccess(barcode: Barcode) {
            val payload = barcode.rawValue?.trim()?.ifBlank { null }
                ?: barcode.displayValue?.trim()?.ifBlank { null }
            if (payload == null) {
                onError(IllegalStateException("The scanned QR code did not contain a pairing payload."))
            } else {
                onPayload(payload)
            }
        }
    }

    private class QrFailureListener(private val onError: (Throwable) -> Unit) : OnFailureListener {
        override fun onFailure(exception: Exception) {
            onError(exception)
        }
    }

    private class QrCanceledListener(private val onPayload: (String?) -> Unit) : OnCanceledListener {
        override fun onCanceled() {
            onPayload(null)
        }
    }
}
