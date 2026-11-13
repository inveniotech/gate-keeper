// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper

import com.fasterxml.jackson.databind.ObjectMapper
import org.springframework.core.MethodParameter
import org.springframework.http.MediaType
import org.springframework.http.converter.HttpMessageConverter
import org.springframework.http.server.ServerHttpRequest
import org.springframework.http.server.ServerHttpResponse
import org.springframework.http.server.ServletServerHttpRequest
import org.springframework.http.server.ServletServerHttpResponse
import org.springframework.web.bind.annotation.ControllerAdvice
import org.springframework.web.servlet.mvc.method.annotation.ResponseBodyAdvice

/** Transforms controller bodies atomically before JSON conversion. */
@ControllerAdvice
public class GateKeeperResponseAdvice(
    matcher: RuntimeMatcher,
    paths: List<String>,
    private val mapper: ObjectMapper,
) : ResponseBodyAdvice<Any> {
    private val filter: ResponseFilter = ResponseFilter(matcher, paths)
    private val hasProtectedPaths: Boolean = paths.isNotEmpty()
    /** Shared request attribute prevents a servlet hook from redacting an advised body twice. */
    public companion object {
        /** Set only after a complete, successful transformation. */
        public const val FILTERED_ATTRIBUTE: String = "africa.gatekeeper.response.filtered"
    }
    /** Applies to all converter types so a protected non-JSON body fails closed. */
    override fun supports(returnType: MethodParameter, converterType: Class<out HttpMessageConverter<*>>): Boolean = hasProtectedPaths
    /** Converts objects to a private JSON tree and refuses unsupported or already committed responses. */
    override fun beforeBodyWrite(body: Any?, returnType: MethodParameter, selectedContentType: MediaType,
        selectedConverterType: Class<out HttpMessageConverter<*>>, request: ServerHttpRequest, response: ServerHttpResponse): Any? {
        if (!hasProtectedPaths) return body
        val servletRequest = (request as? ServletServerHttpRequest)?.servletRequest
            ?: throw ResponseFilter.ResponseFilterException(false)
        val servletResponse = (response as? ServletServerHttpResponse)?.servletResponse
            ?: throw ResponseFilter.ResponseFilterException(false)
        try {
            if (servletResponse.isCommitted) { servletResponse.outputStream.close(); throw ResponseFilter.ResponseFilterException(true) }
            val tree = mapper.convertValue(body, Any::class.java)
            val result = filter.transform(tree, true, false, selectedContentType.toString())
            if (mapper.writeValueAsBytes(result).size > GateKeeperFilter.MAXIMUM_BODY_BYTES) throw ResponseFilter.ResponseFilterException(false)
            servletRequest.setAttribute(FILTERED_ATTRIBUTE, true)
            return result
        } catch (error: RuntimeException) {
            if (error is java.util.concurrent.CancellationException) throw error
            throw ResponseFilter.ResponseFilterException(servletResponse.isCommitted)
        }
    }
}
