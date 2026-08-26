plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
    id("maven-publish")
}

group = providers.gradleProperty("ansightAndroidGroup").orElse("ai.ansight").get()
version = providers.gradleProperty("ansightAndroidVersion").orElse("1.4.0-preview.5").get()

android {
    namespace = "ai.ansight.tools.jnireferencediagnostics"
    compileSdk = 35

    defaultConfig {
        minSdk = 21
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

val dotnetRuntimeDependencies by configurations.creating

dependencies {
    api(project(":ansight-core"))
    implementation("com.squareup.leakcanary:shark-graph:2.14")
    dotnetRuntimeDependencies("com.squareup.leakcanary:shark-graph:2.14") {
        exclude(group = "com.squareup.okio", module = "okio")
    }
    testImplementation("junit:junit:4.13.2")
}

val copyDotnetRuntimeDependencies by tasks.registering(Sync::class) {
    from(dotnetRuntimeDependencies)
    include("shark-*.jar")
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
                artifactId = providers.gradleProperty("ansightAndroidJniReferenceDiagnosticsArtifactId")
                    .orElse("ansight-tools-jnireferencediagnostics-android")
                    .get()
                version = project.version.toString()
            }
        }
    }
}
