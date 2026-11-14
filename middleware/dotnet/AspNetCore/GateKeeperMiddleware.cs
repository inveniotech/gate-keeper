// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Middleware;
using Microsoft.AspNetCore.Http;

namespace InvenioTech.GateKeeper.AspNetCore;

/// <summary>
/// Buffers every response while protected JSON paths are configured, then lets
/// the shared guard transform it before the client sees a byte. A response the
/// minimal-API or MVC filter already transformed is passed through once.
/// </summary>
public sealed class GateKeeperMiddleware : IMiddleware
{
    private readonly GateKeeperOptions options;
    private readonly ResponseFilter filter;

    /// <summary>Creates the middleware from the registered start-up settings.</summary>
    /// <param name="options">The registered options; an empty path list leaves responses untouched.</param>
    /// <param name="filter">The compiled response filter over the verified runtime.</param>
    public GateKeeperMiddleware(GateKeeperOptions options, ResponseFilter filter)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.filter = filter ?? throw new ArgumentNullException(nameof(filter));
    }

    /// <summary>Runs the rest of the pipeline and writes the transformed body.</summary>
    /// <param name="context">The current request.</param>
    /// <param name="next">The remaining pipeline.</param>
    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (options.ProtectedJsonPointers.Count == 0 || GateKeeperResponseGuard.IsFiltered(context))
        {
            return next(context);
        }

        return GateKeeperResponseGuard.BufferAsync(context, () => next(context), filter);
    }
}
