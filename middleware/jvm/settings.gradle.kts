pluginManagement { repositories { gradlePluginPortal(); mavenCentral() } }
rootProject.name = "gate-keeper-jvm"
include("core", "logback", "log4j2", "spring-boot")
include("sample-spring-boot")
project(":sample-spring-boot").projectDir = file("../../samples/spring-boot")
