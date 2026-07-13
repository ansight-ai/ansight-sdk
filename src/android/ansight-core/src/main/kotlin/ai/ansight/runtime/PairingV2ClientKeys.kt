package ai.ansight.runtime

import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import androidx.annotation.RequiresApi
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.Signature
import java.security.spec.ECGenParameterSpec

internal data class PairingClientKey(
    val keyId: String,
    val publicKeyBase64: String,
    val persistent: Boolean,
    private val signer: (ByteArray) -> ByteArray,
) {
    fun signP1363(content: String): String {
        val der = signer(content.toByteArray(Charsets.UTF_8))
        return OkioCompat.encodeBase64(PairingV2Crypto.derToP1363(der))
    }
}

internal fun interface PairingClientKeyProvider {
    fun getOrCreate(scope: String): PairingClientKey
}

internal object AndroidPairingClientKeyProvider : PairingClientKeyProvider {
    override fun getOrCreate(scope: String): PairingClientKey {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) {
            throw IllegalArgumentException(
                "Secure protocol v2 pairing requires Android 6.0 (API 23) or newer for a non-exportable P-256 client key.",
            )
        }
        val alias = aliasFor(scope)
        return loadOrCreateAndroidKey(alias)
    }

    @RequiresApi(Build.VERSION_CODES.M)
    private fun loadOrCreateAndroidKey(alias: String): PairingClientKey {
        val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        if (!keyStore.containsAlias(alias)) {
            val generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, "AndroidKeyStore")
            val specification = KeyGenParameterSpec.Builder(
                alias,
                KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY,
            )
                .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
                .setDigests(KeyProperties.DIGEST_SHA256)
                .build()
            generator.initialize(specification)
            generator.generateKeyPair()
        }

        val publicKey = requireNotNull(keyStore.getCertificate(alias)?.publicKey) { "Android Keystore did not return a pairing public key." }
        val privateKey = requireNotNull(keyStore.getKey(alias, null) as? PrivateKey) { "Android Keystore did not return a pairing private key." }
        return key(publicKey.encoded, privateKey, persistent = true)
    }

    private fun key(publicKeyBytes: ByteArray, privateKey: PrivateKey, persistent: Boolean): PairingClientKey {
        return PairingClientKey(
            keyId = PairingV2Crypto.sha256Base64Url(publicKeyBytes),
            publicKeyBase64 = OkioCompat.encodeBase64(publicKeyBytes),
            persistent = persistent,
        ) { content ->
            Signature.getInstance("SHA256withECDSA").run {
                initSign(privateKey)
                update(content)
                sign()
            }
        }
    }

    private fun aliasFor(scope: String): String {
        val suffix = PairingV2Crypto.sha256Base64Url(scope.toByteArray(Charsets.UTF_8))
            .replace('-', '_')
        return "ai.ansight.pairing.v2.$suffix"
    }

}
