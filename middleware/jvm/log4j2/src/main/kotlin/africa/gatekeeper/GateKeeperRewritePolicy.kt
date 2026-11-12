// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper

import java.util.concurrent.atomic.AtomicReference
import org.apache.logging.log4j.core.LogEvent
import org.apache.logging.log4j.core.appender.rewrite.RewritePolicy
import org.apache.logging.log4j.core.config.plugins.Plugin
import org.apache.logging.log4j.core.config.plugins.PluginFactory
import org.apache.logging.log4j.core.impl.Log4jLogEvent
import org.apache.logging.log4j.message.SimpleMessage

/** Installs one immutable matcher before the application starts logging. */
public object GateKeeperLog4j {
    private val installed: AtomicReference<RuntimeMatcher> = AtomicReference()
    /** Refuses replacement so concurrent log events share the same verified installation. */
    @JvmStatic public fun install(matcher: RuntimeMatcher): Unit {
        check(installed.compareAndSet(null, matcher)) { "A logging matcher is already installed. Configure it once before logging." }
    }
    internal fun matcher(): RuntimeMatcher? = installed.get()
}

/** Log4j2 Rewrite plugin that copies events and replaces only their formatted message. */
@Plugin(name = "GateKeeperRewritePolicy", category = "Core", elementType = "rewritePolicy", printObject = true)
public class GateKeeperRewritePolicy private constructor() : RewritePolicy {
    /** Plugin factory and content-free failure sentence. */
    public companion object {
        /** Returned when redaction cannot complete safely. */
        public const val FAILURE_MESSAGE: String = "The log message could not be redacted safely."
        /** Creates the policy after the application has installed its matcher. */
        @JvmStatic @PluginFactory public fun createPolicy(): GateKeeperRewritePolicy = GateKeeperRewritePolicy()
    }
    /** Returns a new event; redaction failure substitutes one fixed safe sentence. */
    override fun rewrite(source: LogEvent): LogEvent {
        val safe = try { GateKeeperLog4j.matcher()?.redact(source.message.formattedMessage) ?: FAILURE_MESSAGE }
            catch (error: RuntimeException) { FAILURE_MESSAGE }
        return Log4jLogEvent.Builder(source).setMessage(SimpleMessage(safe)).build()
    }
}
