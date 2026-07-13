package ai.ansight.runtime

import okhttp3.OkHttpClient
import java.security.cert.CertificateException
import java.security.cert.CertificateParsingException
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

internal object PairingV2Tls {
    private const val ServerAuthenticationEku = "1.3.6.1.5.5.7.3.1"
    private const val AnyExtendedKeyUsage = "2.5.29.37.0"

    fun createClient(tlsSpkiSha256: String): OkHttpClient {
        val trustManager = ExactSpkiTrustManager(tlsSpkiSha256)
        val context = SSLContext.getInstance("TLS")
        context.init(null, arrayOf(trustManager), null)
        return OkHttpClient.Builder()
            .sslSocketFactory(context.socketFactory, trustManager)
            .readTimeout(0, TimeUnit.MILLISECONDS)
            .build()
    }

    internal class ExactSpkiTrustManager(tlsSpkiSha256: String) : X509TrustManager {
        private val expectedPin = PairingV2Crypto.decodeBase64Url(tlsSpkiSha256, 32)

        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {
            throw CertificateException("Client certificates are not accepted by the pairing client trust manager.")
        }

        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
            val leaf = chain?.firstOrNull() ?: throw CertificateException("The WSS server did not provide a leaf certificate.")
            leaf.checkValidity()
            val extendedKeyUsage = try {
                leaf.extendedKeyUsage
            } catch (error: CertificateParsingException) {
                throw CertificateException("The WSS leaf certificate has an invalid Extended Key Usage extension.", error)
            }
            if (extendedKeyUsage != null &&
                ServerAuthenticationEku !in extendedKeyUsage &&
                AnyExtendedKeyUsage !in extendedKeyUsage
            ) {
                throw CertificateException("The WSS leaf certificate is not valid for Server Authentication.")
            }
            val actualPin = PairingV2Crypto.sha256(leaf.publicKey.encoded)
            if (!PairingV2Crypto.fixedTimeEquals(expectedPin, actualPin)) {
                throw CertificateException("The WSS leaf certificate SPKI pin does not match the signed pairing offer.")
            }
        }

        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }
}
