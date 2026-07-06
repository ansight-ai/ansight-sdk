plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

val ansightHarnessDependencyMode = providers.gradleProperty("ansightHarnessDependencyMode")
    .orElse(providers.environmentVariable("ANSIGHT_HARNESS_DEPENDENCY_MODE"))
    .orElse("local")

val ansightHarnessVersion = providers.gradleProperty("ansightHarnessVersion")
    .orElse(providers.environmentVariable("ANSIGHT_HARNESS_VERSION"))
    .orElse(providers.gradleProperty("ansightAndroidVersion"))

android {
    namespace = "ai.ansight.harness"
    compileSdk = 35

    defaultConfig {
        applicationId = "ai.ansight.harness"
        minSdk = 23
        targetSdk = 35
        versionCode = 1
        versionName = "1.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    when (val dependencyMode = ansightHarnessDependencyMode.get().trim().lowercase()) {
        "local" -> implementation(project(":ansight"))
        "published" -> implementation(
            "${providers.gradleProperty("ansightAndroidGroup").get()}:" +
                "${providers.gradleProperty("ansightAndroidArtifactId").get()}:" +
                ansightHarnessVersion.get()
        )
        else -> throw GradleException(
            "Unsupported ansightHarnessDependencyMode '$dependencyMode'. Use 'local' or 'published'."
        )
    }

    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
}
