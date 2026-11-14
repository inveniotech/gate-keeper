# Spring Boot sample

The Java 17 application starts a real Spring Boot context with an explicitly
verified `RuntimeMatcher`, an application-owned `ConsentCheck` bean and Logback's
Gate-Keeper converter. `gatekeeper.paths=/customer/bvn` enables the adapter's
auto-configuration and bounded response advice. Its controller logs and returns
the existing synthetic corpus value, reporting consent separately from redaction.

From the repository root, with Java 21 and Gradle 8.14.3, after preparing the
development media described in [the sample guide](../README.md):

```powershell
gradle -p middleware/jvm :sample-spring-boot:verifySample
```

The acceptance entry point starts Boot, invokes the controller through Spring's
real MockMvc pipeline with the Boot-created Gate-Keeper advice and checks both
granted and denied answers. It checks rendered Logback output and the final JSON,
then closes the context. It opens no listening socket. The ordinary application
entry point accepts a signed pack directory and a public-only key catalogue;
the acceptance task also exercises the filtered HTTP response. No development
key is trusted implicitly and no runtime CLI or licence check is required.

The Gradle subproject is attached to the existing JVM build, reusing its pinned
framework dependencies. To regenerate its dependency lock after an approved
dependency change, run `gradle -p middleware/jvm :sample-spring-boot:verifySample --write-locks`.
