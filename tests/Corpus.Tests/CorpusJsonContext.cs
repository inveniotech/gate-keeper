// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// The System.Text.Json source-generation context for the harness's own
/// serialisation (rule 7). A test project never publishes a reflector, so the
/// corpus keeps the engine's AOT-clean shape.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CorpusExpectedEntry[]))]
internal sealed partial class CorpusJsonContext : JsonSerializerContext;
