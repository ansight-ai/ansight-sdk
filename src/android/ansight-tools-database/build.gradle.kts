plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
    id("maven-publish")
}

group = providers.gradleProperty("ansightAndroidGroup").orElse("ai.ansight").get()
version = providers.gradleProperty("ansightAndroidVersion").orElse("1.0.2-preview.3").get()

android {
    namespace = "ai.ansight.tools.database"
    compileSdk = 35

    defaultConfig {
        minSdk = 16
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
}

afterEvaluate {
    publishing {
        publications {
            create<MavenPublication>("release") {
                from(components["release"])
                groupId = project.group.toString()
                artifactId = providers.gradleProperty("ansightAndroidDatabaseArtifactId").orElse("ansight-tools-database-android").get()
                version = project.version.toString()
            }
        }
    }
}
