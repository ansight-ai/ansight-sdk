plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
    id("maven-publish")
}

group = providers.gradleProperty("ansightAndroidGroup").orElse("ai.ansight").get()
version = providers.gradleProperty("ansightAndroidVersion").orElse("1.3.0-preview.4").get()

val ansightAndroidArtifactId = providers
    .gradleProperty("ansightAndroidCoreArtifactId")
    .orElse("ansight-core-android")

android {
    namespace = "ai.ansight.runtime"
    compileSdk = 35
    experimentalProperties["android.ndk.suppressMinSdkVersionError"] = 21

    buildFeatures {
        buildConfig = true
    }

    defaultConfig {
        minSdk = 16
        consumerProguardFiles("consumer-rules.pro")
        buildConfigField("String", "ANSIGHT_SDK_VERSION", "\"${project.version}\"")

        externalNativeBuild {
            cmake {
                cFlags += "-std=c11"
            }
        }
    }

    externalNativeBuild {
        cmake {
            path = file("src/main/cpp/CMakeLists.txt")
        }
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

val copyDotnetRuntimeDependencies by tasks.registering(Copy::class) {
    from(configurations.named("releaseRuntimeClasspath"))
    include("Java-WebSocket-*.jar", "slf4j-api-*.jar")
    into(layout.buildDirectory.dir("dotnet-runtime"))
}

tasks.matching { it.name == "assembleRelease" }.configureEach {
    finalizedBy(copyDotnetRuntimeDependencies)
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
