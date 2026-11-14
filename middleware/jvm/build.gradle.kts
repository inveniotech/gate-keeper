import org.jetbrains.kotlin.gradle.dsl.JvmTarget
import org.jetbrains.kotlin.gradle.tasks.KotlinCompile

plugins {
    base
    kotlin("jvm") version "2.1.21" apply false
    kotlin("kapt") version "2.1.21" apply false
}
allprojects {
    group = "africa.gatekeeper"
    version = file("${rootDir}/../version.txt").readText().trim()
    repositories { mavenCentral() }
    dependencyLocking { lockAllConfigurations() }
}
subprojects {
    apply(plugin = "java-library")
    extensions.configure<JavaPluginExtension> {
        sourceCompatibility = JavaVersion.toVersion(if (name == "spring-boot") 17 else 15)
        targetCompatibility = sourceCompatibility
        withSourcesJar()
    }
    tasks.withType<JavaCompile>().configureEach { options.release.set(if (project.name == "spring-boot") 17 else 15) }
    tasks.withType<Jar>().configureEach { archiveBaseName.set("gate-keeper-${project.name}"); isPreserveFileTimestamps = false; isReproducibleFileOrder = true }
    tasks.withType<JavaExec>().configureEach {
        providers.environmentVariable("GATEKEEPER_TEST_JAVA_HOME").orNull?.let { testHome ->
            executable = file("$testHome/bin/java").absolutePath
        }
    }
    tasks.withType<KotlinCompile>().configureEach {
        compilerOptions { jvmTarget.set(if (project.name == "spring-boot") JvmTarget.JVM_17 else JvmTarget.JVM_15) }
    }
}
tasks.register("verifyAdapters") { dependsOn(":logback:check", ":log4j2:check", ":spring-boot:check", ":spring-boot:compileBoot4Kotlin", "compileJavaSamples") }
tasks.register<JavaCompile>("compileCoreSample") {
    dependsOn(":core:classes")
    source("sample/CoreExample.java")
    classpath = project(":core").extensions.getByType<SourceSetContainer>()["main"].output
    destinationDirectory.set(layout.buildDirectory.dir("sample/core")); options.release.set(15)
    sourceCompatibility = "15"; targetCompatibility = "15"
}
tasks.register<JavaCompile>("compileJavaSamples") {
    dependsOn("compileCoreSample", ":logback:classes", ":log4j2:classes", ":spring-boot:classes")
    source("sample/HooksExample.java")
    classpath = files(subprojects.map { it.extensions.getByType<SourceSetContainer>()["main"].output }) +
        project(":spring-boot").configurations.getByName("compileClasspath") +
        project(":logback").configurations.getByName("compileClasspath") + project(":log4j2").configurations.getByName("compileClasspath")
    destinationDirectory.set(layout.buildDirectory.dir("sample/hooks")); options.release.set(17)
    sourceCompatibility = "17"; targetCompatibility = "17"
}
