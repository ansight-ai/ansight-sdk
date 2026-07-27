plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
    id("maven-publish")
}

group = providers.gradleProperty("ansightAndroidGroup").orElse("ai.ansight").get()
version = providers.gradleProperty("ansightAndroidVersion").orElse("1.0.2-preview.4").get()

android {
    namespace = "ai.ansight.pairing"
    compileSdk = 35

    defaultConfig {
        minSdk = 23
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_1_8
        targetCompatibility = JavaVersion.VERSION_1_8
    }

    kotlinOptions {
        jvmTarget = "1.8"
    }

    publishing {
        singleVariant("release") {
            withSourcesJar()
            withJavadocJar()
        }
    }
}

dependencies {
    api(project(":ansight-core"))
    implementation("com.google.android.gms:play-services-code-scanner:16.1.0")
    api("com.google.android.material:material:1.4.0")
}

afterEvaluate {
    publishing {
        publications {
            create<MavenPublication>("release") {
                from(components["release"])
                groupId = project.group.toString()
                artifactId = providers.gradleProperty("ansightAndroidPairingArtifactId").orElse("ansight-pairing-android").get()
                version = project.version.toString()
            }
        }
    }
}
