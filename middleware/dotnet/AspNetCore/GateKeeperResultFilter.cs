// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace InvenioTech.GateKeeper.AspNetCore;

/// <summary>
/// The MVC global result filter. It executes the result into the shared bounded
/// buffer, so the body is transformed before MVC writes to the client.
/// </summary>
public sealed class GateKeeperResultFilter : IAsyncResultFilter
{
    private readonly GateKeeperOptions options;
    private readonly ResponseFilter filter;

    /// <summary>Creates the filter from the registered start-up settings.</summary>
    /// <param name="options">The registered options; an empty path list leaves results untouched.</param>
    /// <param name="filter">The compiled response filter over the verified runtime.</param>
    public GateKeeperResultFilter(GateKeeperOptions options, ResponseFilter filter)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.filter = filter ?? throw new ArgumentNullException(nameof(filter));
    }

    /// <summary>Runs the MVC result and writes the transformed body.</summary>
    /// <param name="context">The result execution context.</param>
    /// <param name="next">The delegate that executes the result.</param>
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (options.ProtectedJsonPointers.Count == 0 || GateKeeperResponseGuard.IsFiltered(context.HttpContext))
        {
            return next();
        }

        return GateKeeperResponseGuard.BufferAsync(context.HttpContext, () => next(), filter);
    }
}

/// <summary>Adds the MVC result filter to the application's global filter list.</summary>
internal sealed class GateKeeperMvcOptionsSetup : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options) => options.Filters.AddService<GateKeeperResultFilter>();
}
