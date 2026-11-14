// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper

import com.fasterxml.jackson.databind.ObjectMapper
import org.springframework.boot.autoconfigure.AutoConfiguration
import org.springframework.boot.context.properties.bind.Bindable
import org.springframework.boot.context.properties.bind.Binder
import org.springframework.context.annotation.Bean
import org.springframework.context.annotation.Conditional
import org.springframework.context.annotation.Condition
import org.springframework.context.annotation.ConditionContext
import org.springframework.core.env.Environment
import org.springframework.core.type.AnnotatedTypeMetadata

/** Registers servlet and controller hooks when protected JSON paths are configured. A verified matcher bean is required. */
@AutoConfiguration
@Conditional(GateKeeperPathsCondition::class)
public class GateKeeperAutoConfiguration {
    private fun paths(environment: Environment): List<String> = Binder.get(environment)
        .bind("gatekeeper.paths", Bindable.listOf(String::class.java)).orElseThrow { IllegalArgumentException("Configure protected JSON paths before starting.") }
    /** Requires an installation-created matcher; missing verified pack configuration refuses application start-up. */
    @Bean public fun gatekeeperFilter(matcher: RuntimeMatcher, environment: Environment): GateKeeperFilter {
        requireVerified(matcher)
        return GateKeeperFilter(matcher, paths(environment), ObjectMapper())
    }
    /** Registers atomic controller response filtering before the message converter runs. */
    @Bean public fun gatekeeperResponseAdvice(matcher: RuntimeMatcher, environment: Environment): GateKeeperResponseAdvice {
        requireVerified(matcher)
        return GateKeeperResponseAdvice(matcher, paths(environment), ObjectMapper())
    }
    private fun requireVerified(matcher: RuntimeMatcher) {
        if (!matcher.isVerifiedInstallation) throw RuntimeMatcher.PackLoadException("No verified installation is configured. Create the matcher with Installation.load before starting.")
    }
}

/** Enables both scalar and indexed property binding without silently ignoring YAML path lists. */
public class GateKeeperPathsCondition : Condition {
    /** Uses the same binder as bean creation; malformed configured paths fail during binding. */
    override fun matches(context: ConditionContext, metadata: AnnotatedTypeMetadata): Boolean =
        Binder.get(context.environment).bind("gatekeeper.paths", Bindable.listOf(String::class.java)).isBound
}
