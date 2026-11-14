plugins { kotlin("jvm"); kotlin("kapt") }
kotlin { explicitApi() }
dependencies {
    api(project(":core"))
    compileOnly("org.apache.logging.log4j:log4j-core:2.24.3")
    kapt("org.apache.logging.log4j:log4j-core:2.24.3")
    testImplementation("org.apache.logging.log4j:log4j-core:2.24.3")
}
val probe by tasks.registering(JavaExec::class) { dependsOn(tasks.testClasses); classpath = sourceSets.test.get().runtimeClasspath; mainClass.set("africa.gatekeeper.Log4jChecks") }
tasks.check { dependsOn(probe) }
