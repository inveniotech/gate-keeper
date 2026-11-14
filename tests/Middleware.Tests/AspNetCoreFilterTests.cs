// SPDX-License-Identifier: Apache-2.0

using System.Text;
using InvenioTech.GateKeeper.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace InvenioTech.GateKeeper.Middleware.Tests;

public sealed class AspNetCoreFilterTests
{
    [Fact]
    public async Task EndpointFilter_Should_WrapTheResult_When_TheEndpointReturnsAJsonResult()
    {
        var context = NewContext();
        var filter = new GateKeeperEndpointFilter(Options(["/value"]), TestRuntimes.Matcher().Filter(["/value"]));

        var wrapped = await filter.InvokeAsync(EndpointFilterInvocationContext.Create(context),
            _ => ValueTask.FromResult<object?>(new JsonBodyResult("""{"value":"BVN 32826314843"}""")));

        await ((IResult)wrapped!).ExecuteAsync(context);

        Body(context).Should().Be("""{"value":"BVN [REDACTED:pii.bvn]"}""");
        context.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task EndpointFilter_Should_ReturnTheObjectUnchanged_When_TheResultIsNotAnIResult()
    {
        var context = NewContext();
        var filter = new GateKeeperEndpointFilter(Options(["/value"]), TestRuntimes.Matcher().Filter(["/value"]));
        var plain = new object();

        var result = await filter.InvokeAsync(EndpointFilterInvocationContext.Create(context),
            _ => ValueTask.FromResult<object?>(plain));

        result.Should().BeSameAs(plain);
        Body(context).Should().BeEmpty();
    }

    [Fact]
    public async Task EndpointFilter_Should_LeaveTheResultAlone_When_NoPathIsConfigured()
    {
        var context = NewContext();
        var filter = new GateKeeperEndpointFilter(Options([]), TestRuntimes.Matcher().Filter([]));
        var json = new JsonBodyResult("""{"value":"BVN 32826314843"}""");

        var result = await filter.InvokeAsync(EndpointFilterInvocationContext.Create(context),
            _ => ValueTask.FromResult<object?>(json));

        result.Should().BeSameAs(json);
        Body(context).Should().BeEmpty();
    }

    [Fact]
    public async Task EndpointFilter_Should_NotWrapTwice_When_AnInnerFilterAlreadyWrapped()
    {
        var context = NewContext();
        var options = Options(["/value"]);
        var filter = new GateKeeperEndpointFilter(options, TestRuntimes.Matcher().Filter(["/value"]));
        var json = new JsonBodyResult("""{"value":"BVN 32826314843"}""");

        var inner = await filter.InvokeAsync(EndpointFilterInvocationContext.Create(context),
            _ => ValueTask.FromResult<object?>(json));
        var outer = await filter.InvokeAsync(EndpointFilterInvocationContext.Create(context),
            _ => ValueTask.FromResult(inner));

        outer.Should().BeSameAs(inner);
    }

    [Fact]
    public async Task EndpointFilter_Should_ThrowResponseFilterException_When_TheResultContentTypeIsNotJson()
    {
        var context = NewContext();
        var filter = new GateKeeperEndpointFilter(Options(["/value"]), TestRuntimes.Matcher().Filter(["/value"]));

        var wrapped = await filter.InvokeAsync(EndpointFilterInvocationContext.Create(context),
            _ => ValueTask.FromResult<object?>(new JsonBodyResult("plain text", "text/plain")));
        Func<Task> execute = () => ((IResult)wrapped!).ExecuteAsync(context);

        (await execute.Should().ThrowAsync<ResponseFilterException>()).Which.AbortRequired.Should().BeFalse();
        Body(context).Should().BeEmpty();
    }

    [Fact]
    public async Task MvcResultFilter_Should_RedactTheResultBody_When_TheActionResultWritesJson()
    {
        var context = NewContext();
        var filter = new GateKeeperResultFilter(Options(["/value"]), TestRuntimes.Matcher().Filter(["/value"]));
        var action = new ActionContext(context, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata> { filter };
        var executing = new ResultExecutingContext(action, filters, new JsonBodyResult("""{"value":"BVN 32826314843"}"""), Controller);

        await filter.OnResultExecutionAsync(executing, async () =>
        {
            await executing.Result!.ExecuteResultAsync(action);
            return new ResultExecutedContext(action, filters, executing.Result, Controller);
        });

        Body(context).Should().Be("""{"value":"BVN [REDACTED:pii.bvn]"}""");
        context.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task MvcResultFilter_Should_SkipTheResult_When_NoPathIsConfigured()
    {
        var context = NewContext();
        var filter = new GateKeeperResultFilter(Options([]), TestRuntimes.Matcher().Filter([]));
        var action = new ActionContext(context, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata> { filter };
        var executing = new ResultExecutingContext(action, filters, new JsonBodyResult("BVN 32826314843"), Controller);

        await filter.OnResultExecutionAsync(executing, async () =>
        {
            await executing.Result!.ExecuteResultAsync(action);
            return new ResultExecutedContext(action, filters, executing.Result, Controller);
        });

        Body(context).Should().Be("BVN 32826314843");
    }

    [Fact]
    public async Task MvcResultFilter_Should_ThrowResponseFilterException_When_TheResultBodyIsNotJson()
    {
        var context = NewContext();
        var filter = new GateKeeperResultFilter(Options(["/value"]), TestRuntimes.Matcher().Filter(["/value"]));
        var action = new ActionContext(context, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata> { filter };
        var executing = new ResultExecutingContext(action, filters, new JsonBodyResult("plain text", "text/plain"), Controller);

        Func<Task> run = () => filter.OnResultExecutionAsync(executing, async () =>
        {
            await executing.Result!.ExecuteResultAsync(action);
            return new ResultExecutedContext(action, filters, executing.Result, Controller);
        });

        (await run.Should().ThrowAsync<ResponseFilterException>()).Which.AbortRequired.Should().BeFalse();
        Body(context).Should().BeEmpty();
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string Body(DefaultHttpContext context)
    {
        var stream = (MemoryStream)context.Response.Body;
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static GateKeeperOptions Options(IReadOnlyList<string> pointers) => new() { ProtectedJsonPointers = pointers };

    /// <summary>The framework declares the controller argument non-nullable; the tests pass a stand-in.</summary>
    private static readonly object Controller = new();

    /// <summary>A stand-in for the framework's JSON result that writes a fixed body.</summary>
    private sealed class JsonBodyResult(string body, string contentType = "application/json") : IResult, IActionResult
    {
        public Task ExecuteAsync(HttpContext httpContext) => Write(httpContext.Response);

        public Task ExecuteResultAsync(ActionContext context) => Write(context.HttpContext.Response);

        private Task Write(HttpResponse response)
        {
            response.ContentType = contentType;
            return response.Body.WriteAsync(Encoding.UTF8.GetBytes(body), response.HttpContext.RequestAborted).AsTask();
        }
    }
}

