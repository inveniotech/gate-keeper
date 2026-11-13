plugins { kotlin("jvm") }
kotlin { explicitApi() }
dependencies {
    api(project(":core"))
    compileOnly("ch.qos.logback:logback-classic:1.5.18")
    testImplementation("ch.qos.logback:logback-classic:1.5.18")
}
val probe by tasks.registering(JavaExec::class) { dependsOn(tasks.testClasses); classpath = sourceSets.test.get().runtimeClasspath; mainClass.set("africa.gatekeeper.LogbackChecks") }
tasks.check { dependsOn(probe) }
