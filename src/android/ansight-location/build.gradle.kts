plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
    id("maven-publish")
}

group = providers.gradleProperty("ansightAndroidGroup").orElse("ai.ansight").get()
version = providers.gradleProperty("ansightAndroidVersion").orElse("1.3.0-preview.4").get()

android {
    namespace = "ai.ansight.location"
    compileSdk = 35
    defaultConfig { minSdk = 16 }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_1_8
        targetCompatibility = JavaVersion.VERSION_1_8
    }
    kotlinOptions { jvmTarget = "1.8" }
    publishing {
        singleVariant("release") {
            withSourcesJar()
            withJavadocJar()
        }
    }
}

dependencies {
    api(project(":ansight-core"))
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
}

afterEvaluate {
    publishing {
        publications {
            create<MavenPublication>("release") {
                from(components["release"])
                groupId = project.group.toString()
                artifactId = providers.gradleProperty("ansightAndroidLocationArtifactId")
                    .orElse("ansight-location-android").get()
                version = project.version.toString()
                pom {
                    name.set("Ansight Android Location")
                    description.set("Explicit observed-location recording for existing Ansight sessions.")
                    url.set("https://github.com/ansight-ai/ansight-sdk")
                }
            }
        }
    }
}
