package ai.ansight.runtime

import org.junit.Assert.assertThrows
import org.junit.Test
import java.math.BigInteger
import java.security.KeyPairGenerator
import java.security.Principal
import java.security.PublicKey
import java.security.cert.CertificateExpiredException
import java.security.cert.CertificateNotYetValidException
import java.security.cert.X509Certificate
import java.util.Date

class PairingV2TlsTest {
    @Test
    fun exactLeafSpkiPinIsRequired() {
        val publicKey = KeyPairGenerator.getInstance("EC").apply { initialize(256) }.generateKeyPair().public
        val certificate = TestCertificate(publicKey)
        val matching = PairingV2Tls.ExactSpkiTrustManager(PairingV2Crypto.sha256Base64Url(publicKey.encoded))
        matching.checkServerTrusted(arrayOf(certificate), "ECDHE_ECDSA")

        val mismatched = PairingV2Tls.ExactSpkiTrustManager(PairingV2Crypto.encodeBase64Url(ByteArray(32) { 1 }))
        assertThrows(java.security.cert.CertificateException::class.java) {
            mismatched.checkServerTrusted(arrayOf(certificate), "ECDHE_ECDSA")
        }
    }

    @Test
    fun serverAuthenticationEkuIsRequiredWhenEkuIsPresent() {
        val publicKey = KeyPairGenerator.getInstance("EC").apply { initialize(256) }.generateKeyPair().public
        val certificate = TestCertificate(publicKey, extendedUsage = listOf("1.3.6.1.5.5.7.3.2"))
        val trustManager = PairingV2Tls.ExactSpkiTrustManager(PairingV2Crypto.sha256Base64Url(publicKey.encoded))

        assertThrows(java.security.cert.CertificateException::class.java) {
            trustManager.checkServerTrusted(arrayOf(certificate), "ECDHE_ECDSA")
        }
    }

    private class TestCertificate(
        private val key: PublicKey,
        private val extendedUsage: List<String>? = listOf("1.3.6.1.5.5.7.3.1"),
        private val validFrom: Date = Date(System.currentTimeMillis() - 60_000),
        private val validUntil: Date = Date(System.currentTimeMillis() + 60_000),
    ) : X509Certificate() {
        override fun checkValidity() = checkValidity(Date())
        override fun checkValidity(date: Date) {
            if (date.before(validFrom)) throw CertificateNotYetValidException()
            if (date.after(validUntil)) throw CertificateExpiredException()
        }
        override fun getExtendedKeyUsage(): MutableList<String>? = extendedUsage?.toMutableList()
        override fun getPublicKey(): PublicKey = key
        override fun getNotBefore(): Date = validFrom
        override fun getNotAfter(): Date = validUntil
        override fun getVersion(): Int = 3
        override fun getSerialNumber(): BigInteger = BigInteger.ONE
        override fun getIssuerDN(): Principal = Principal { "CN=Test" }
        override fun getSubjectDN(): Principal = Principal { "CN=Test" }
        override fun getTBSCertificate(): ByteArray = byteArrayOf()
        override fun getSignature(): ByteArray = byteArrayOf()
        override fun getSigAlgName(): String = "SHA256withECDSA"
        override fun getSigAlgOID(): String = "1.2.840.10045.4.3.2"
        override fun getSigAlgParams(): ByteArray? = null
        override fun getIssuerUniqueID(): BooleanArray? = null
        override fun getSubjectUniqueID(): BooleanArray? = null
        override fun getKeyUsage(): BooleanArray? = null
        override fun getBasicConstraints(): Int = -1
        override fun getEncoded(): ByteArray = byteArrayOf()
        override fun verify(key: PublicKey?) = Unit
        override fun verify(key: PublicKey?, sigProvider: String?) = Unit
        override fun toString(): String = "TestCertificate"
        override fun getCriticalExtensionOIDs(): MutableSet<String>? = null
        override fun getNonCriticalExtensionOIDs(): MutableSet<String>? = null
        override fun getExtensionValue(oid: String?): ByteArray? = null
        override fun hasUnsupportedCriticalExtension(): Boolean = false
    }
}
