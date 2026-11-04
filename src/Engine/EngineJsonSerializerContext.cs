// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The System.Text.Json source-generation context for every serialised engine
/// type, so no reflection-based serialisation exists anywhere in the engine or
/// the command-line interface (rule 7). The exact key order of the evidence
/// file and of the SARIF profile is the writer's job (P1-11); this context
/// already carries the camelCase names and the string forms of the
/// <c>error</c> / <c>warning</c> / <c>note</c> severities the formats fix, and
/// it omits keys whose value is absent, so an optional citation section the
/// pack left out and a fingerprint produced without a key are not written as
/// nulls.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RulePack))]
[JsonSerializable(typeof(Rule))]
[JsonSerializable(typeof(RuleMatcher))]
[JsonSerializable(typeof(RulePatternMatcher))]
[JsonSerializable(typeof(RuleListMatcher))]
[JsonSerializable(typeof(RuleShapePart))]
[JsonSerializable(typeof(RuleCharRange))]
[JsonSerializable(typeof(RuleLocation))]
[JsonSerializable(typeof(RuleValidator))]
[JsonSerializable(typeof(MergedRules))]
[JsonSerializable(typeof(LoadedPack))]
[JsonSerializable(typeof(LoadedRule))]
[JsonSerializable(typeof(Citation))]
[JsonSerializable(typeof(RedactionOptions))]
[JsonSerializable(typeof(ProcessorInfo))]
[JsonSerializable(typeof(ScanResult))]
[JsonSerializable(typeof(Finding))]
[JsonSerializable(typeof(Location))]
[JsonSerializable(typeof(Fingerprint))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
public sealed partial class EngineJsonSerializerContext : JsonSerializerContext;
