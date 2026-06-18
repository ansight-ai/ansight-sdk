import org.gradle.api.publish.PublishingExtension
import org.gradle.api.publish.maven.MavenPublication
import org.gradle.plugins.signing.SigningExtension

plugins {
    id("com.android.application") version "8.7.3" apply false
    id("com.android.library") version "8.7.3" apply false
    id("org.jetbrains.kotlin.android") version "1.8.22" apply false
}

subprojects {
    plugins.withId("maven-publish") {
        plugins.apply("signing")

        extensions.configure<PublishingExtension>("publishing") {
            val repositoryUrl = providers
                .gradleProperty("ansightMavenUrl")
                .orElse(providers.environmentVariable("ANSIGHT_MAVEN_URL"))

            if (repositoryUrl.isPresent) {
                repositories {
                    maven {
                        name = providers
                            .gradleProperty("ansightMavenRepositoryName")
                            .orElse(providers.environmentVariable("ANSIGHT_MAVEN_REPOSITORY_NAME"))
                            .orElse("ansight")
                            .get()
                        url = uri(repositoryUrl.get())

                        val repositoryUsername = providers
                            .gradleProperty("ansightMavenUsername")
                            .orElse(providers.environmentVariable("ANSIGHT_MAVEN_USERNAME"))
                        val repositoryPassword = providers
                            .gradleProperty("ansightMavenPassword")
                            .orElse(providers.environmentVariable("ANSIGHT_MAVEN_PASSWORD"))

                        if (repositoryUsername.isPresent || repositoryPassword.isPresent) {
                            credentials {
                                username = repositoryUsername.orElse("").get()
                                password = repositoryPassword.orElse("").get()
                            }
                        }
                    }
                }
            }

            publications.withType<MavenPublication>().configureEach {
                pom {
                    name.convention("Ansight Android ${project.name}")
                    description.convention("Ansight native Android SDK module ${project.name}.")
                    url.convention("https://github.com/ansight-ai/ansight-sdk")

                    licenses {
                        license {
                            name.convention("Ansight SDK Source-Available License")
                            url.convention("https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE")
                            distribution.convention("repo")
                        }
                    }

                    developers {
                        developer {
                            id.convention("ansight")
                            name.convention("Ansight AI")
                            email.convention("dev@ansight.ai")
                        }
                    }

                    scm {
                        connection.convention("scm:git:https://github.com/ansight-ai/ansight-sdk.git")
                        developerConnection.convention("scm:git:ssh://git@github.com/ansight-ai/ansight-sdk.git")
                        url.convention("https://github.com/ansight-ai/ansight-sdk")
                    }
                }
            }
        }

        extensions.configure<SigningExtension>("signing") {
            val signingKey = providers
                .gradleProperty("signingInMemoryKey")
                .orElse(providers.environmentVariable("ORG_GRADLE_PROJECT_signingInMemoryKey"))
                .orElse(providers.environmentVariable("ANSIGHT_GPG_SIGNING_KEY"))
            val signingPassword = providers
                .gradleProperty("signingInMemoryKeyPassword")
                .orElse(providers.environmentVariable("ORG_GRADLE_PROJECT_signingInMemoryKeyPassword"))
                .orElse(providers.environmentVariable("ANSIGHT_GPG_SIGNING_PASSWORD"))

            isRequired = signingKey.isPresent && signingPassword.isPresent

            if (isRequired) {
                useInMemoryPgpKeys(signingKey.get(), signingPassword.get())
                sign(extensions.getByType<PublishingExtension>().publications)
            }
        }
    }
}
