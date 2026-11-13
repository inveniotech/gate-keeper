# Spring Boot artefact

`gate-keeper-spring-boot` targets Java 17 and provides the servlet filter,
controller advice and auto-configuration. Configure `gatekeeper.paths` as a
comma-separated value or indexed list and provide an Installation-created
matcher bean. An unverified matcher refuses start-up. No configured paths means
no auto-configured hooks. Direct hook constructors support low-level testing.

The servlet fallback buffers at most 1 MiB and requires known-length JSON.
Advice and servlet filtering never send partially transformed protected bodies.
See the parent README for pointer syntax and abort behaviour.

Spring Boot, Spring MVC, Jackson 2 and Jakarta Servlet are application-provided
compile-only dependencies. The runtime jar depends on core and Kotlin's standard
library. Run `gradle -p middleware/jvm :spring-boot:check` for servlet, advice,
auto-configuration checks and the second compile against Boot 4.
