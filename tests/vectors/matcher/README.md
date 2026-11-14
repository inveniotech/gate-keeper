# Matcher conformance vectors

These vectors are the first cross-stack lines for the matcher engine (P1-06, IQ-14). Phase 3's conformance kit (P3-00) lifts them; until that kit exists, `MatcherConformanceVectorTests` is the check that the C# engine agrees with the file.

Each case is one file of synthetic text and one rule. `expected` lists the matches by rule identifier, 1-based line and column, and, when the case cares, the location kind and the suppression hints. An empty `expected` array is a near-miss. The matched text is never a field (rule 10).

The behaviours the other engines have to reproduce, because the rule-pack format leaves the mechanics in prose:

- Letters in literals, alternations and keywords fold ASCII A–Z only.
- A keyword is a substring. The window is the number of scalar values between the keyword and the shape, on either side.
- A rejected match resumes one scalar value after its start. An accepted match resumes at its end.
- `digit` and `alnum` boundaries look at the ASCII neighbour on each side of the shape. The ends of the text are boundaries.
- The `email` boundary rejects a shape whose leading neighbour is an ASCII letter, digit, `.`, `_`, `%`, `+` or `-`, and whose trailing neighbour is an ASCII letter, digit, `_`, `%`, `+` or `-`. The trailing set leaves out `.` so a sentence-ending dot after the domain does not reject the match (IQ-91). A pack that uses the boundary must set `minEngineVersion` to 0.2.0 or newer.
- A list entry matches only as a whole token. A token is a maximal run of ASCII letters, digits, `.`, `-`, `_` and `+`. A shorter entry does not match inside a longer one.
- `suppressionHints` are help text. They never suppress a match.

The file is hand-written. It is the source the test reads, not a generated artefact.
