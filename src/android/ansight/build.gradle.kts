plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
    id("maven-publish")
}

group = providers.gradleProperty("ansightAndroidGroup").orElse("ai.ansight").get()
version = providers.gradleProperty("ansightAndroidVersion").orElse("1.4.0-preview.3").get()

android {
    namespace = "ai.ansight"
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
    api(project(":ansight-pairing"))
    api(project(":ansight-tools-visualtree"))
    api(project(":ansight-tools-filesystem"))
    api(project(":ansight-tools-file-descriptor-diagnostics"))
    api(project(":ansight-tools-jni-reference-diagnostics"))
    api(project(":ansight-tools-preferences"))
    api(project(":ansight-tools-securestorage"))
    api(project(":ansight-tools-database"))
    api(project(":ansight-tools-reflection"))

    testImplementation("junit:junit:4.13.2")
}

afterEvaluate {
    publishing {
        publications {
            create<MavenPublication>("release") {
                from(components["release"])
                groupId = project.group.toString()
                artifactId = providers.gradleProperty("ansightAndroidArtifactId").orElse("ansight-android").get()
                version = project.version.toString()
            }
        }
    }
}
