// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper

import ch.qos.logback.classic.pattern.ClassicConverter
import ch.qos.logback.classic.spi.ILoggingEvent

/** Redacts formatted messages using the matcher installed on the Logback context. */
public class RedactingConverter : ClassicConverter() {
    /** Context key and safe, content-free fallback shared by logging hooks. */
    public companion object {
        /** Logger-context object key for the verified runtime matcher. */
        public const val MATCHER_KEY: String = "africa.gatekeeper.matcher"
        /** Returned when no matcher exists or redaction fails. */
        public const val FAILURE_MESSAGE: String = "The log message could not be redacted safely."
    }
    /** Returns a redacted formatted message; never returns failed input. */
    override fun convert(event: ILoggingEvent): String = try {
        val matcher = context?.getObject(MATCHER_KEY) as? RuntimeMatcher
        matcher?.redact(event.formattedMessage) ?: FAILURE_MESSAGE
    } catch (error: RuntimeException) { FAILURE_MESSAGE }
}
