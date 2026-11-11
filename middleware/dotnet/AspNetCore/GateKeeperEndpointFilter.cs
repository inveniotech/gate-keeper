// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Middleware;
using Microsoft.AspNetCore.Http;

namespace InvenioTech.GateKeeper.AspNetCore;

/// <summary>
/// The minimal-API endpoint filter. It executes the endpoint's <see cref="IResult"/>
/// into the shared bounded buffer, so redaction happens before the client write.
/// A bare object result is returned unchanged and left to the outer middleware;
/// minimal-API's reflection-based serialisation is never entered here.
/// </summary>
public sealed class GateKeeperEndpointFilter : IEndpointFilter
{
    private readonly GateKeeperOptions options;
    private readonly ResponseFilter filter;

    /// <summary>Creates the filter from the registered start-up settings.</summary>
    /// <param name="options">The registered options; an empty path list leaves results untouched.</param>
    /// <param name="filter">The compiled response filter over the verified runtime.</param>
    public GateKeeperEndpointFilter(GateKeeperOptions options, ResponseFilter filter)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.filter = filter ?? throw new ArgumentNullException(nameof(filter));
    }

    /// <summary>Wraps an JSON result so it is filtered when the framework executes it.</summary>
    /// <param name="context">The endpoint call including its <see cref="HttpContext"/>.</param>
    /// <param name="next">The rest of the endpoint filter pipeline.</param>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var result = await next(context);
        if (options.ProtectedJsonPointers.Count == 0
            || GateKeeperResponseGuard.IsFiltered(context.HttpContext)
            || result is not IResult inner
            || result is GateKeeperFilteringResult)
        {
            return result;
        }

        return new GateKeeperFilteringResult(inner, filter);
    }

    /// <summary>The result wrapper that buffers, transforms and then writes the endpoint result.</summary>
    private sealed class GateKeeperFilteringResult(IResult inner, ResponseFilter filter) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) =>
            GateKeeperResponseGuard.BufferAsync(httpContext, () => inner.ExecuteAsync(httpContext), filter);
    }
}
