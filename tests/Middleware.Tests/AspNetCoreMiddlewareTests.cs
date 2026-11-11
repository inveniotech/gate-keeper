// SPDX-License-Identifier: Apache-2.0

using System.Text;
using InvenioTech.GateKeeper.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace InvenioTech.GateKeeper.Middleware.Tests;

public sealed class AspNetCoreMiddlewareTests
{
    [Fact]
    public async Task Middleware_Should_RedactTheProtectedString_When_TheResponseIsBoundedJson()
    {
        var context = NewContext("application/json");
        var middleware = new GateKeeperMiddleware(Options(["/customer/bvn"]), TestRuntimes.Matcher().Filter(["/customer/bvn"]));

        await middleware.InvokeAsync(context, async current =>
        {
            await current.Response.WriteAsync("""{"customer":{"bvn":"BVN 32826314843"}}""", Encoding.UTF8);
        });

        Body(context).Should().Be("""{"customer":{"bvn":"BVN [REDACTED:pii.bvn]"}}""");
        context.Response.ContentLength.Should().Be(Body(context).Length);
    }

    [Fact]
    public async Task Middleware_Should_RedactEveryElement_When_ThePathCarriesAWildcard()
    {
        var context = NewContext("application/json");
        var middleware = new GateKeeperMiddleware(Options(["/customers/*/bvn"]), TestRuntimes.Matcher().Filter(["/customers/*/bvn"]));

        await middleware.InvokeAsync(context, async current =>
        {
            await current.Response.WriteAsync("""{"customers":[{"bvn":"BVN 32826314843"},{"bvn":"plain"}]}""", Encoding.UTF8);
        });

        Body(context).Should().Be("""{"customers":[{"bvn":"BVN [REDACTED:pii.bvn]"},{"bvn":"plain"}]}""");
    }

    [Fact]
    public async Task Middleware_Should_LeaveTheResponseUntouched_When_NoPathIsConfigured()
    {
        var context = NewContext(null);
        var middleware = new GateKeeperMiddleware(Options([]), TestRuntimes.Matcher().Filter([]));

        await middleware.InvokeAsync(context, async current =>
        {
            await current.Response.WriteAsync("BVN 32826314843", Encoding.UTF8);
        });

        Body(context).Should().Be("BVN 32826314843");
    }

    [Fact]
    public async Task Middleware_Should_LeaveAnEmptyBodyUntouched_When_TheEndpointWritesNothing()
    {
        var context = NewContext("application/json");
        var middleware = new GateKeeperMiddleware(Options(["/customer/bvn"]), TestRuntimes.Matcher().Filter(["/customer/bvn"]));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Body(context).Should().BeEmpty();
    }

    [Fact]
    public async Task Middleware_Should_PassTheBufferThrough_When_AnInnerHookAlreadyFiltered()
    {
        var context = NewContext("application/json");
        var filter = TestRuntimes.Matcher().Filter(["/value"]);
        var middleware = new GateKeeperMiddleware(Options(["/value"]), filter);

        await middleware.InvokeAsync(context, async current =>
        {
            await current.Response.WriteAsync("""{"value":"BVN [REDACTED:pii.bvn]"}""", Encoding.UTF8);
            GateKeeperResponseGuard.MarkFiltered(current);
        });

        Body(context).Should().Be("""{"value":"BVN [REDACTED:pii.bvn]"}""");
    }

    [Fact]
    public async Task Middleware_Should_ThrowResponseFilterException_When_TheContentTypeIsNotJson()
    {
        var context = NewContext("text/plain");
        var middleware = new GateKeeperMiddleware(Options(["/value"]), TestRuntimes.Matcher().Filter(["/value"]));

        Func<Task> call = () => middleware.InvokeAsync(context, async current =>
        {
            await current.Response.WriteAsync("plain text", Encoding.UTF8);
        });

        (await call.Should().ThrowAsync<ResponseFilterException>()).Which.AbortRequired.Should().BeFalse();
        Body(context).Should().BeEmpty();
    }

    [Fact]
    public async Task Middleware_Should_ThrowResponseFilterException_When_TheBodyIsNotJson()
    {
        var context = NewContext("application/json");
        var middleware = new GateKeeperMiddleware(Options(["/value"]), TestRuntimes.Matcher().Filter(["/value"]));

        Func<Task> call = () => middleware.InvokeAsync(context, async current =>
        {
            await current.Response.WriteAsync("{", Encoding.UTF8);
        });

        (await call.Should().ThrowAsync<ResponseFilterException>()).Which.AbortRequired.Should().BeFalse();
        Body(context).Should().BeEmpty();
    }

    [Fact]
    public async Task Middleware_Should_ThrowResponseFilterException_When_TheBodyIsNotUtf8()
    {
        var context = NewContext("application/json");
        var middleware = new GateKeeperMiddleware(Options(["/value"]), TestRuntimes.Matcher().Filter(["/value"]));

        Func<Task> call = () => middleware.InvokeAsync(context, current =>
        {
            var bytes = new byte[] { 0xC3, 0x28 };
            return current.Response.Body.WriteAsync(bytes, current.RequestAborted).AsTask();
        });

        (await call.Should().ThrowAsync<ResponseFilterException>()).Which.AbortRequired.Should().BeFalse();
        Body(context).Should().BeEmpty();
    }

    [Fact]
    public async Task Middleware_Should_ThrowResponseFilterException_When_TheBodyExceedsTheScalarCap()
    {
        var context = NewContext("application/json");
        var middleware = new GateKeeperMiddleware(Options(["/value"]), TestRuntimes.Matcher().Filter(["/value"]));
        var oversized = "{\"value\":\"" + new string('a', 65_537) + "\"}";

        Func<Task> call = () => middleware.InvokeAsync(context, async current =>
        {
            await current.Response.WriteAsync(oversized, Encoding.UTF8);
        });

        (await call.Should().ThrowAsync<ResponseFilterException>()).Which.AbortRequired.Should().BeFalse();
        Body(context).Should().BeEmpty();
    }

    [Fact]
    public async Task Middleware_Should_ThrowResponseFilterException_When_TheOutputExpandsBeyondTheCap()
    {
        var context = NewContext("application/json");
        var matcher = TestRuntimes.TokenMatcher("x");
        var middleware = new GateKeeperMiddleware(Options(["/*/value"]), matcher.Filter(["/*/value"]));
        var dense = "[" + string.Join(",", Enumerable.Repeat("{\"value\":\"x\"}", 4_681)) + "]";

        Func<Task> call = () => middleware.InvokeAsync(context, async current =>
        {
            await current.Response.WriteAsync(dense, Encoding.UTF8);
        });

        (await call.Should().ThrowAsync<ResponseFilterException>()).Which.AbortRequired.Should().BeFalse();
        Body(context).Should().BeEmpty();
    }

    private static DefaultHttpContext NewContext(string? contentType)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        if (contentType is not null)
        {
            context.Response.ContentType = contentType;
        }

        return context;
    }

    private static string Body(DefaultHttpContext context)
    {
        var stream = (MemoryStream)context.Response.Body;
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static GateKeeperOptions Options(IReadOnlyList<string> pointers) => new() { ProtectedJsonPointers = pointers };
}
