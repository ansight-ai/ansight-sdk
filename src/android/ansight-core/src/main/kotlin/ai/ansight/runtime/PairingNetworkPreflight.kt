package ai.ansight.runtime

import android.app.Application
import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.os.Build

internal enum class PairingNetworkPreflightStatus {
    Unknown,
    Connected,
    NotConnected,
    Cellular,
}

internal object PairingNetworkPreflight {
    fun getStatus(application: Application?): PairingNetworkPreflightStatus {
        if (application == null) {
            return PairingNetworkPreflightStatus.Unknown
        }

        return runCatching {
            val connectivityManager = application.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager
                ?: return PairingNetworkPreflightStatus.Unknown

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                val network = connectivityManager.activeNetwork
                    ?: return PairingNetworkPreflightStatus.NotConnected
                val capabilities = connectivityManager.getNetworkCapabilities(network)
                    ?: return PairingNetworkPreflightStatus.Unknown

                when {
                    capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) ||
                        capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET) ->
                        PairingNetworkPreflightStatus.Connected
                    capabilities.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) ->
                        PairingNetworkPreflightStatus.Cellular
                    else -> PairingNetworkPreflightStatus.Unknown
                }
            } else {
                @Suppress("DEPRECATION")
                val networkInfo = connectivityManager.activeNetworkInfo
                    ?: return PairingNetworkPreflightStatus.NotConnected
                @Suppress("DEPRECATION")
                when {
                    !networkInfo.isConnected -> PairingNetworkPreflightStatus.NotConnected
                    networkInfo.type == ConnectivityManager.TYPE_WIFI ||
                        networkInfo.type == ConnectivityManager.TYPE_ETHERNET ->
                        PairingNetworkPreflightStatus.Connected
                    networkInfo.type == ConnectivityManager.TYPE_MOBILE ->
                        PairingNetworkPreflightStatus.Cellular
                    else -> PairingNetworkPreflightStatus.Unknown
                }
            }
        }.getOrDefault(PairingNetworkPreflightStatus.Unknown)
    }
}
