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
include(":ansight-tools-visualtree")
include(":ansight-tools-filesystem")
include(":ansight-tools-preferences")
include(":ansight-tools-securestorage")
include(":ansight-tools-database")
include(":ansight-tools-reflection")
include(":ansight-pairing")
include(":ansight")
include(":harness")
