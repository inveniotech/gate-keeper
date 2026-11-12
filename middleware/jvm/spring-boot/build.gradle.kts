plugins { kotlin("jvm") }
kotlin { explicitApi() }
val boot4 by sourceSets.creating { java.setSrcDirs(emptyList<String>()) }
kotlin.sourceSets.named("boot4") { kotlin.srcDir("src/main/kotlin") }
dependencies {
    api(project(":core"))
    compileOnly("org.springframework.boot:spring-boot-autoconfigure:3.5.0")
    compileOnly("org.springframework:spring-webmvc:6.2.7")
    compileOnly("jakarta.servlet:jakarta.servlet-api:6.0.0")
    compileOnly("com.fasterxml.jackson.core:jackson-databind:2.19.0")
    testImplementation("org.springframework.boot:spring-boot-autoconfigure:3.5.0")
    testImplementation("org.springframework:spring-webmvc:6.2.7")
    testImplementation("org.springframework:spring-test:6.2.7")
    testImplementation("jakarta.servlet:jakarta.servlet-api:6.0.0")
    testImplementation("com.fasterxml.jackson.core:jackson-databind:2.19.0")
    add(boot4.compileOnlyConfigurationName, project(":core"))
    add(boot4.compileOnlyConfigurationName, "org.springframework.boot:spring-boot-autoconfigure:4.0.0")
    add(boot4.compileOnlyConfigurationName, "org.springframework:spring-webmvc:7.0.1")
    add(boot4.compileOnlyConfigurationName, "jakarta.servlet:jakarta.servlet-api:6.1.0")
    add(boot4.compileOnlyConfigurationName, "com.fasterxml.jackson.core:jackson-databind:2.19.0")
}
val probe by tasks.registering(JavaExec::class) { dependsOn(tasks.testClasses); workingDir(rootProject.file("../..")); classpath = sourceSets.test.get().runtimeClasspath; mainClass.set("africa.gatekeeper.SpringChecks") }
tasks.check { dependsOn(probe, "compileBoot4Kotlin") }
