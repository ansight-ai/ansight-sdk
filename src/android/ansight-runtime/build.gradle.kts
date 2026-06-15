plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
    id("maven-publish")
}

group = providers.gradleProperty("ansightAndroidGroup").orElse("ai.ansight").get()
version = providers.gradleProperty("ansightAndroidVersion").orElse("0.1.0-pre1").get()

val ansightAndroidArtifactId = providers
    .gradleProperty("ansightAndroidArtifactId")
    .orElse("ansight-runtime-android")

android {
    namespace = "ai.ansight.runtime"
    compileSdk = 35

    defaultConfig {
        minSdk = 16
        consumerProguardFiles("consumer-rules.pro")
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
        }
    }
}

dependencies {
    implementation("androidx.annotation:annotation:1.9.1")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")

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
                    name.set("Ansight Android Runtime")
                    description.set("Native Android runtime for the Ansight protocol.")
                    url.set("https://github.com/ansight-ai/ansight-sdk")
                    licenses {
                        license {
                            name.set("Ansight SDK Source-Available License")
                            url.set("https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE")
                            distribution.set("repo")
                        }
                    }
                }
            }
        }
    }
}
