// SPDX-License-Identifier: Apache-2.0
package africa.gatekeeper

import com.fasterxml.jackson.databind.ObjectMapper
import jakarta.servlet.*
import jakarta.servlet.http.*
import java.io.*
import java.nio.charset.StandardCharsets
import java.nio.charset.Charset
import java.util.Locale

/** Buffers protected servlet JSON responses within a fixed host bound before committing bytes. */
public class GatekeeperFilter(
    matcher: RuntimeMatcher,
    paths: List<String>,
    private val mapper: ObjectMapper,
) : Filter {
    private val filter: ResponseFilter = ResponseFilter(matcher, paths)
    private val hasProtectedPaths: Boolean = paths.isNotEmpty()
    /** Host parsing limit; selected strings retain the core scalar limit. */
    public companion object {
        /** Maximum buffered and serialised JSON response size. */
        public const val MAXIMUM_BODY_BYTES: Int = 1024 * 1024
    }
    /** Never sends original or partially transformed protected bytes, including on failure. */
    override fun doFilter(request: ServletRequest, response: ServletResponse, chain: FilterChain): Unit {
        if (!hasProtectedPaths) { chain.doFilter(request, response); return }
        val http = response as? HttpServletResponse ?: throw ResponseFilter.ResponseFilterException(false)
        if (http.isCommitted) { http.outputStream.close(); throw ResponseFilter.ResponseFilterException(true) }
        val buffered = BufferedResponse(http)
        try {
            chain.doFilter(request, buffered)
            buffered.finish()
            if (http.isCommitted) throw ResponseFilter.ResponseFilterException(true)
            val input = buffered.bytes.toByteArray()
            val advised = request.getAttribute(GatekeeperResponseAdvice.FILTERED_ATTRIBUTE) == true
            val output = if (advised) input else {
                val media = buffered.contentType
                val length = buffered.declaredLength
                if (length == null || length != input.size.toLong()) throw ResponseFilter.ResponseFilterException(false)
                val type = media?.substringBefore(';')?.trim()?.lowercase(Locale.ROOT) ?: ""
                if (type != "application/json" && !(type.startsWith("application/") && type.endsWith("+json"))) throw ResponseFilter.ResponseFilterException(false)
                val tree = if (buffered.usedWriter) mapper.readValue(String(input, Charset.forName(buffered.characterEncoding)), Any::class.java)
                    else mapper.readValue(input, Any::class.java)
                mapper.writeValueAsBytes(filter.transform(tree, true, false, media))
            }
            if (output.size > MAXIMUM_BODY_BYTES) throw ResponseFilter.ResponseFilterException(false)
            if (!advised) http.characterEncoding = StandardCharsets.UTF_8.name()
            http.setContentLength(output.size)
            http.outputStream.write(output)
        } catch (error: Exception) {
            if (http.isCommitted) http.outputStream.close()
            if (error is InterruptedException) { Thread.currentThread().interrupt(); throw error }
            if (error is java.util.concurrent.CancellationException) throw error
            throw ResponseFilter.ResponseFilterException(http.isCommitted)
        }
    }
    private class BufferedResponse(response: HttpServletResponse) : HttpServletResponseWrapper(response) {
        val bytes = ByteArrayOutputStream()
        var declaredLength: Long? = null
        private var writer: PrintWriter? = null
        val usedWriter: Boolean get() = writer != null
        private var streamUsed = false
        private val stream = object : ServletOutputStream() {
            override fun isReady(): Boolean = true
            override fun setWriteListener(listener: WriteListener): Unit = throw ResponseFilter.ResponseFilterException(false)
            override fun write(value: Int) { if (bytes.size() >= MAXIMUM_BODY_BYTES) throw ResponseFilter.ResponseFilterException(false); bytes.write(value) }
            override fun write(value: ByteArray, offset: Int, length: Int) {
                if (length > MAXIMUM_BODY_BYTES - bytes.size()) throw ResponseFilter.ResponseFilterException(false)
                bytes.write(value, offset, length)
            }
        }
        override fun getOutputStream(): ServletOutputStream {
            check(writer == null) { "Use one response output API." }; streamUsed = true; return stream
        }
        override fun getWriter(): PrintWriter {
            check(!streamUsed) { "Use one response output API." }
            if (writer == null) writer = PrintWriter(OutputStreamWriter(stream, characterEncoding ?: StandardCharsets.UTF_8.name()))
            return writer!!
        }
        fun finish() { writer?.flush(); if (writer?.checkError() == true) throw ResponseFilter.ResponseFilterException(false) }
        override fun setContentLength(length: Int) { declaredLength = length.toLong() }
        override fun setContentLengthLong(length: Long) { declaredLength = length }
        override fun setHeader(name: String, value: String?) {
            if (name.equals("Content-Length", true)) declaredLength = value?.toLongOrNull() else super.setHeader(name, value)
        }
        override fun addHeader(name: String, value: String?) {
            if (name.equals("Content-Length", true)) declaredLength = value?.toLongOrNull() else super.addHeader(name, value)
        }
        override fun flushBuffer(): Unit = throw ResponseFilter.ResponseFilterException(false)
        override fun sendError(status: Int): Unit = throw ResponseFilter.ResponseFilterException(false)
        override fun sendError(status: Int, message: String?): Unit = throw ResponseFilter.ResponseFilterException(false)
        override fun sendRedirect(location: String): Unit = throw ResponseFilter.ResponseFilterException(false)
        override fun resetBuffer() { bytes.reset() }
        override fun reset() { super.reset(); bytes.reset(); declaredLength = null }
    }
}
