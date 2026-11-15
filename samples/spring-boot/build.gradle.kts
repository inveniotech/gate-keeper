plugins { application }

java { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
tasks.withType<JavaCompile>().configureEach { options.release.set(17) }
application { mainClass.set("sample.SampleApplication") }

dependencies {
    implementation(project(":core"))
    implementation(project(":spring-boot"))
    implementation(project(":logback"))
    implementation("org.springframework.boot:spring-boot-autoconfigure:3.5.0")
    implementation("org.springframework:spring-webmvc:6.2.7")
    implementation("jakarta.servlet:jakarta.servlet-api:6.0.0")
    implementation("com.fasterxml.jackson.core:jackson-databind:2.19.0")
    implementation("ch.qos.logback:logback-classic:1.5.18")
    testImplementation("org.springframework:spring-test:6.2.7")
}

val verifySample by tasks.registering(JavaExec::class) {
    dependsOn(tasks.testClasses)
    workingDir(rootProject.file("../.."))
    classpath = sourceSets.test.get().runtimeClasspath
    mainClass.set("sample.SampleChecks")
}
tasks.check { dependsOn(verifySample) }
