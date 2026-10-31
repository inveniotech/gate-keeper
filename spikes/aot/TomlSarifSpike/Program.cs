// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Spikes.Aot.TomlSarif;
using SarifKit.IO;
using SarifKit.Models;
using Tomlyn;
using Tomlyn.Serialization;

// Parse a representative configuration file with the source-generated context.
// The TOML text mirrors the sample file ndpa-check.toml in this folder.
string toml = """
repository = "acme/payments"

[scan]
profile = "default"
max-file-size-kb = 1024
include-submodules = false
exclusions = [ ".git", "node_modules", "vendor" ]

[fail-on]
level = "error"

[severity-overrides]
"pii.bvn" = "warning"

[packs]
local = [ "local/rules.toml" ]
""";

NdpachConfig? config = TomlSerializer.Deserialize(toml, NdpachTomlContext.Default.NdpachConfig);
if (config is null)
{
    Console.WriteLine("TOML parse returned null");
    return 2;
}

Console.WriteLine($"TOML parsed: repository={config.Repository}, profile={config.Scan.Profile}, " +
    $"max-file-size-kb={config.Scan.MaxFileSizeKb}, fail-on={config.FailOn.Level}, " +
    $"severity-overrides={config.SeverityOverrides.Count}, exclusions={config.Scan.Exclusions.Count}, local-packs={config.Packs.Local.Count}");

// Round-trip through the source-generated writer to confirm both directions work.
string roundTripped = TomlSerializer.Serialize(config, NdpachTomlContext.Default.NdpachConfig);
Console.WriteLine($"TOML round-trip: {roundTripped.Split('\n').Length} lines");

// Build and write a minimal SARIF 2.1.0 log with SarifKit.
var log = new SarifLog
{
    Runs =
    [
        new SarifRun
        {
            Tool = new Tool
            {
                Driver = new ToolComponent
                {
                    Name = "gatekeeper-ndpa-check",
                    Version = "0.0.0",
                    Rules = [new ReportingDescriptor { Id = "pii.bvn" }],
                },
            },
            Results =
            [
                new Result
                {
                    RuleId = "pii.bvn",
                    RuleIndex = 0,
                    Kind = ResultKind.Fail,
                    Level = FailureLevel.Warning,
                    Message = new Message { Text = "An 11-digit value resembles a Bank Verification Number." },
                    Locations =
                    [
                        new Location
                        {
                            PhysicalLocation = new PhysicalLocation
                            {
                                ArtifactLocation = new ArtifactLocation { Uri = "src/Program.cs" },
                                Region = new Region { StartLine = 10, StartColumn = 1 },
                            },
                        },
                    ],
                },
            ],
        },
    ],
};

string json = SarifWriter.WriteToString(log);
Console.WriteLine($"SARIF serialised: {json.Length} characters");

string sarifPath = Path.Combine(Path.GetTempPath(), "gatekeeper-aot-spike.sarif");
using (FileStream output = File.Create(sarifPath))
{
    SarifWriter.Write(log, output);
}

Console.WriteLine($"SARIF written: {sarifPath} ({new FileInfo(sarifPath).Length} bytes)");

// Prove the written file reads back to the same model.
using (FileStream input = File.OpenRead(sarifPath))
{
    SarifLog roundTrippedLog = SarifReader.Read(input);
    Console.WriteLine($"SARIF read back: {roundTrippedLog.Runs.Count} run(s), " +
        $"{roundTrippedLog.Runs[0].Results?.Count ?? 0} result(s)");
}

Console.WriteLine("RESULT: PASS");
return 0;
