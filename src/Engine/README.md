# Rule engine

Placeholder for the rule engine library. The .NET middleware will reuse this library, so it is a class library rather than part of the command-line interface. Matching, named validators and pack loading arrive in Phase 1.

## Public contract

`EngineModule.Status` is `not-started`. There is no other public member.

## Dependencies

None.

## How to test

`dotnet test --filter PlaceholderTests.Engine_module_reports_not_started`
