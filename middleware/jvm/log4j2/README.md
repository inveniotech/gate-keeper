# Log4j2 artefact

`gate-keeper-log4j2` supplies the generated `GatekeeperRewritePolicy` plugin at
Java 15 bytecode. Call `GatekeeperLog4j.install` once before logging and route
messages through a Rewrite appender. The policy copies events and replaces the
formatted message; failures use a fixed safe sentence.

The application supplies Log4j2 2.x. Core and the Kotlin standard library are
runtime dependencies; Log4j2 is never shaded. Run
`gradle -p middleware/jvm :log4j2:check` for the synthetic policy checks.
