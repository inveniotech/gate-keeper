// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using SarifKit.IO;
using SarifKit.Models;
using Tomlyn;
using Tomlyn.Serialization;

// This project links every component the CLI will ship with, so its published
// binary size is a realistic estimate of the real command-line tool. Each
// section performs one representative call so the trimmer keeps the code.

// 1. Runtime regex (NonBacktracking).
Regex digits = new(@"[0-9]{11}", RegexOptions.NonBacktracking);
int matches = digits.Count("bvn 12345678901 account 23456789012");
Console.WriteLine($"regex matches: {matches}");

// 2. Ed25519 sign and verify (BouncyCastle).
byte[] seed = new byte[32];
var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
Ed25519PublicKeyParameters publicKey = privateKey.GeneratePublicKey();
var signer = new Ed25519Signer();
signer.Init(true, privateKey);
byte[] message = System.Text.Encoding.UTF8.GetBytes("gatekeeper-ndpa-check");
signer.BlockUpdate(message, 0, message.Length);
byte[] signature = signer.GenerateSignature();
var verifier = new Ed25519Signer();
verifier.Init(false, publicKey);
verifier.BlockUpdate(message, 0, message.Length);
Console.WriteLine($"ed25519 verify: {verifier.VerifySignature(signature)}");

// 3. TOML configuration (Tomlyn source generator).
string toml = """
repository = "acme/payments"
[scan]
profile = "default"
""";
Config config = TomlSerializer.Deserialize(toml, ConfigTomlContext.Default.Config)
    ?? throw new InvalidDataException("TOML parse returned null");
Console.WriteLine($"toml repository: {config.Repository}");

// 4. System.Text.Json source generation.
string json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.Config);
Console.WriteLine($"json: {json}");

// 5. SARIF writing (SarifKit).
var log = new SarifLog
{
    Runs =
    [
        new SarifRun
        {
            Tool = new Tool { Driver = new ToolComponent { Name = "gatekeeper-ndpa-check", Version = "0.0.0" } },
            Results = [new Result { RuleId = "pii.bvn", Level = FailureLevel.Warning, Message = new Message { Text = "sample" } }],
        },
    ],
};
Console.WriteLine($"sarif: {SarifWriter.WriteToString(log).Length} characters");

Console.WriteLine("RESULT: PASS");
return 0;

internal sealed class Config
{
    public string Repository { get; set; } = string.Empty;

    public ScanSection Scan { get; set; } = new();
}

internal sealed class ScanSection
{
    public string Profile { get; set; } = "default";
}

[JsonSerializable(typeof(Config))]
internal partial class ConfigJsonContext : JsonSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower)]
[TomlSerializable(typeof(Config))]
internal partial class ConfigTomlContext : TomlSerializerContext
{
}
