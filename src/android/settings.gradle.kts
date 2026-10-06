pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "ansight-android"

include(":ansight-core")
include(":ansight-annotations")
include(":ansight-motion")
include(":ansight-dotnet-bridge")
include(":ansight-tools-visualtree")
include(":ansight-tools-filesystem")
include(":ansight-tools-file-descriptor-diagnostics")
include(":ansight-tools-jni-reference-diagnostics")
include(":ansight-tools-preferences")
include(":ansight-tools-clipboard")
include(":ansight-tools-securestorage")
include(":ansight-tools-database")
include(":ansight-tools-reflection")
include(":ansight-pairing")
include(":ansight")
include(":harness")
project(":harness").projectDir = file("../../test-apps/core/android")

include(":motion-test-app")
project(":motion-test-app").projectDir = file("../../test-apps/motion/android")

include(":ansight-purchases-googleplay")
