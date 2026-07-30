plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
    id("maven-publish")
}

group = providers.gradleProperty("ansightAndroidGroup").orElse("ai.ansight").get()
version = providers.gradleProperty("ansightAndroidVersion").orElse("1.0.2-preview.8").get()

val ansightAndroidArtifactId = providers
    .gradleProperty("ansightAndroidCoreArtifactId")
    .orElse("ansight-core-android")

android {
    namespace = "ai.ansight.runtime"
    compileSdk = 35

    buildFeatures {
        buildConfig = true
    }

    defaultConfig {
        minSdk = 16
        consumerProguardFiles("consumer-rules.pro")
        buildConfigField("String", "ANSIGHT_SDK_VERSION", "\"${project.version}\"")
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
    implementation("androidx.annotation:annotation:1.6.0")
    implementation("org.java-websocket:Java-WebSocket:1.6.0") {
        exclude(group = "org.slf4j", module = "slf4j-api")
    }
    implementation("org.slf4j:slf4j-api:1.7.36")

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
}

afterEvaluate {
    publishing {
        publications {
            create<MavenPublication>("release") {
                from(components["release"])
                groupId = project.group.toString()
                artifactId = ansightAndroidArtifactId.get()
                version = project.version.toString()

                pom {
                    name.set("Ansight Android Core")
                    description.set("Core native Android runtime concepts for the Ansight protocol.")
                    url.set("https://github.com/ansight-ai/ansight-sdk")
                }
            }
        }
    }
}
