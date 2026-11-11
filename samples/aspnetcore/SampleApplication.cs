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

namespace InvenioTech.GateKeeper.Sample.AspNetCore;

/// <summary>
/// The acceptance host. It loads the signed pack the installation embeds, builds
/// the real ASP.NET Core application, and writes two log lines: one through the
/// compliance redactor and one through the Serilog enricher. The host is built
/// but never listens; the sample's whole job is to prove the two logging paths
/// redact the committed synthetic value.
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
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var pack = SampleInstallation.ReadPack(packDirectory, error);
        if (pack is null)
        {
            return 1;
        }

        var catalogue = SampleInstallation.ReadKeyCatalogue(keyFile, error);
        if (catalogue is null)
        {
            return 1;
        }

        MergedRules verified;
        try
        {
            verified = RuntimePacks.Load([pack], catalogue);
        }
        catch (PackLoadException failure)
        {
            error.WriteLine(failure.Message);
            return 1;
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddGateKeeper(verified, options => options.ProtectedJsonPointers = ["/customer/bvn"]);
        builder.Services.AddGateKeeperRedaction();
        using var app = builder.Build();
        app.UseGateKeeper();

        // The compliance path: any redaction request for the Gate-Keeper classification goes through the verified rules.
        var redaction = app.Services.GetRequiredService<IRedactorProvider>();
        output.WriteLine("Compliance: " + redaction.GetRedactor(GateKeeperClassifications.Pii).Redact(CommittedValue));

        // The Serilog path: the enricher replaces scalar string properties before any sink renders them.
        using var logger = new LoggerConfiguration()
            .Enrich.With(new GateKeeperEnricher(app.Services.GetRequiredService<RuntimeMatcher>()))
            .WriteTo.Sink(new TextWriterSink(output))
            .CreateLogger();
        logger.Information("Customer record {Record}", CommittedValue);
        return 0;
    }

    /// <summary>A sink that renders the event after every enricher has run.</summary>
    private sealed class TextWriterSink(TextWriter writer) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => writer.WriteLine(logEvent.RenderMessage());
    }
}
