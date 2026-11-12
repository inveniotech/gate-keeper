// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InvenioTech.GateKeeper.Middleware;
using Microsoft.AspNetCore.Http;

namespace InvenioTech.GateKeeper.AspNetCore;

/// <summary>
/// The one bounded buffer the middleware, minimal-API filter and MVC filter
/// share. It parses a private UTF-8 copy with <see cref="JsonNode"/>, applies
/// the existing <see cref="ResponseFilter"/>, and writes the transformed body
/// only after the whole transform succeeds. Nothing here uses reflection, so
/// the hooks stay trim and ahead-of-time safe.
/// </summary>
internal static class GateKeeperResponseGuard
{
    private const int MaximumBodyBytes = RuntimeMatcher.MaximumScalars * 4;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly object Marker = new();

    /// <summary>True when an inner hook already transformed and wrote this response.</summary>
    internal static bool IsFiltered(HttpContext context) => context.Items.ContainsKey(Marker);

    /// <summary>Marks a response as already transformed, the way an inner hook does after a successful write.</summary>
    internal static void MarkFiltered(HttpContext context) => context.Items[Marker] = true;

    /// <summary>
    /// Runs <paramref name="write"/> with the response body redirected into a
    /// bounded private buffer, then transforms and writes the body. A failure
    /// writes no original or partial body; committed headers abort the
    /// connection. An empty body and an unconfigured path list pass through.
    /// </summary>
    internal static async Task BufferAsync(HttpContext context, Func<Task> write, ResponseFilter filter)
    {
        if (IsFiltered(context))
        {
            await write();
            return;
        }
        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        var original = context.Response.Body;
        var buffer = new BoundedBodyStream(MaximumBodyBytes);
        context.Response.Body = buffer;
        try
        {
            await write();
        }
        finally
        {
            context.Response.Body = original;
        }

        if (buffer.Overflowed)
        {
            throw new ResponseFilterException(abortRequired: false);
        }

        if (IsFiltered(context))
        {
            await WriteAsync(context, original, buffer.ToArray());
            return;
        }

        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        var transformed = Transform(buffer.ToArray(), context.Response.ContentType, filter);
        context.Items[Marker] = true;
        await WriteAsync(context, original, transformed);
    }

    private static byte[] Transform(byte[] body, string? contentType, ResponseFilter filter)
    {
        if (body.Length == 0)
        {
            return body;
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            throw new ResponseFilterException(abortRequired: false);
        }

        if (CountScalars(text) > RuntimeMatcher.MaximumScalars)
        {
            throw new ResponseFilterException(abortRequired: false);
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw new ResponseFilterException(abortRequired: false);
        }

        var transformed = filter.Transform(node, lengthKnown: true, headersCommitted: false, contentType: contentType);
        byte[] output;
        try
        {
            output = Serialize(transformed);
        }
        catch (InvalidOperationException)
        {
            throw new ResponseFilterException(abortRequired: false);
        }

        if (CountScalars(output) > RuntimeMatcher.MaximumScalars)
        {
            throw new ResponseFilterException(abortRequired: false);
        }

        return output;
    }

    private static byte[] Serialize(JsonNode? node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            if (node is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                node.WriteTo(writer);
            }
        }

        return stream.ToArray();
    }

    private static async Task WriteAsync(HttpContext context, Stream destination, byte[] body)
    {
        if (body.Length > 0)
        {
            if (context.Response.Headers.ContentEncoding.Count == 0)
            {
                context.Response.ContentLength = body.Length;
            }
            else
            {
                context.Response.ContentLength = null;
            }

            await destination.WriteAsync(body, context.RequestAborted);
        }
    }

    private static int CountScalars(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    private static int CountScalars(byte[] utf8) => CountScalars(StrictUtf8.GetString(utf8));

    /// <summary>A memory buffer that stops at the response cap instead of growing without bound.</summary>
    private sealed class BoundedBodyStream(int maximumBytes) : Stream
    {
        private readonly MemoryStream inner = new();

        internal bool Overflowed { get; private set; }

        internal byte[] ToArray() => inner.ToArray();

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value)
        {
            Ensure(value - inner.Length);
            inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Ensure(count);
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Ensure(buffer.Length);
            inner.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Ensure(count);
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Ensure(buffer.Length);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        private void Ensure(long count)
        {
            if (count <= 0)
            {
                return;
            }

            var end = Math.Max(inner.Length, inner.Position) + count;
            if (end > maximumBytes)
            {
                Overflowed = true;
                throw new ResponseFilterException(abortRequired: false);
            }
        }
    }
}
