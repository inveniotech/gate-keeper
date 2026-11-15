// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.AspNetCore;
using InvenioTech.GateKeeper.Compliance.Redaction;
using InvenioTech.GateKeeper.Engine;
using InvenioTech.GateKeeper.Middleware;
using InvenioTech.GateKeeper.Serilog;
using global::Serilog;
using global::Serilog.Core;
using global::Serilog.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.AspNetCore.Http;

namespace InvenioTech.GateKeeper.Sample.AspNetCore;

/// <summary>
/// The acceptance host. It loads the signed pack the installation embeds, builds
/// the real ASP.NET Core application, and demonstrates compliance and Serilog
/// log redaction, bounded response filtering and application-owned consent.
/// Requests execute in process; the demonstration never opens a listening socket.
/// </summary>
public static class SampleApplication
{
    /// <summary>The synthetic value the committed corpus case carries, next to its BVN label.</summary>
    private const string CommittedValue = "bankVerificationNumber 32826314843";

    /// <summary>Runs the sample and returns the process exit code.</summary>
    /// <param name="packDirectory">The directory that carries the signed pack's release media.</param>
    /// <param name="keyFile">The installation's public-only key catalogue.</param>
    /// <param name="output">Receives the two redacted log lines.</param>
    /// <param name="error">Receives the plain failure sentence; the sample writes nothing else there.</param>
    /// <returns>0 when both lines were logged, otherwise 1.</returns>
    public static int Run(string packDirectory, string keyFile, TextWriter output, TextWriter error)
    {
        using var app = ReadApplication(packDirectory, keyFile, output, error);
        return app is null ? 1 : 0;
    }

    /// <summary>Runs a complete in-process request without opening a listening socket.</summary>
    public static async Task<int> RunAsync(string packDirectory, string keyFile, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        await using var app = ReadApplication(packDirectory, keyFile, output, error);
        if (app is null) return 1;
        var context = new DefaultHttpContext { RequestServices = app.Services, RequestAborted = cancellationToken };
        context.Request.Path = "/customer";
        using var body = new MemoryStream();
        context.Response.Body = body;
        await ((IApplicationBuilder)app).Build()(context);
        await output.WriteLineAsync(System.Text.Encoding.UTF8.GetString(body.ToArray()));
        return 0;
    }

    private static WebApplication? ReadApplication(string packDirectory, string keyFile, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var pack = SampleInstallation.ReadPack(packDirectory, error);
        if (pack is null)
        {
            return null;
        }

        var catalogue = SampleInstallation.ReadKeyCatalogue(keyFile, error);
        if (catalogue is null)
        {
            return null;
        }

        MergedRules verified;
        try
        {
            verified = RuntimePacks.Load([pack], catalogue);
        }
        catch (PackLoadException failure)
        {
            error.WriteLine(failure.Message);
            return null;
        }
        return Create(verified, output);
    }

    /// <summary>Builds the real request pipeline using a verified pack, redacted logs and application-owned consent.</summary>
    public static WebApplication Create(MergedRules verified, TextWriter output)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddGateKeeper(verified, options =>
        {
            options.ProtectedJsonPointers = ["/customer/bvn"];
            options.ConsentProvider = new SampleConsentProvider();
        });
        builder.Services.AddGateKeeperRedaction();
        var app = builder.Build();
        app.UseGateKeeper();
        app.Run(async context =>
        {
            var subject = context.Request.Query["subject"].FirstOrDefault() ?? "synthetic-subject";
            var granted = await context.RequestServices.GetRequiredService<ConsentCheck>()
                .IsGrantedAsync(subject, "acme.marketing-sms", context.RequestAborted);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($"{{\"customer\":{{\"bvn\":\"{CommittedValue}\"}},\"granted\":{(granted ? "true" : "false")}}}", context.RequestAborted);
        });

        // The compliance path: any redaction request for the Gate-Keeper classification goes through the verified rules.
        var redaction = app.Services.GetRequiredService<IRedactorProvider>();
        output.WriteLine("Compliance: " + redaction.GetRedactor(GateKeeperClassifications.Pii).Redact(CommittedValue));

        // The Serilog path: the enricher replaces scalar string properties before any sink renders them.
        using var logger = new LoggerConfiguration()
            .Enrich.With(new GateKeeperEnricher(app.Services.GetRequiredService<RuntimeMatcher>()))
            .WriteTo.Sink(new TextWriterSink(output))
            .CreateLogger();
        logger.Information("Customer record {Record}", CommittedValue);
        return app;
    }

    private sealed class SampleConsentProvider : IConsentProvider
    {
        public ValueTask<ConsentState> GetStateAsync(string subjectId, string purpose, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(subjectId == "synthetic-subject" && purpose == "acme.marketing-sms" ? ConsentState.Granted : ConsentState.Denied);
        }
    }

    /// <summary>A sink that renders the event after every enricher has run.</summary>
    private sealed class TextWriterSink(TextWriter writer) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => writer.WriteLine(logEvent.RenderMessage());
    }
}
