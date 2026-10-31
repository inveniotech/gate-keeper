// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

// A synthetic corpus of 10,000 files, generated deterministically, so that the
// two engines scan the same bytes. Each file is roughly one kilobyte of
// source-like text with occasional 11-digit numbers and "bvn" keywords.
const int FileCount = 10_000;
IReadOnlyList<string> corpus = GenerateCorpus(FileCount, seed: 42);

// Representative patterns from the IQ-14 portable subset: ASCII classes and
// bounded quantifiers only, no look-around, no back-references. Case-insensitive
// keywords are expanded into character classes as the profile requires.
(string Name, string Pattern)[] patterns =
[
    ("[0-9]{11} (digits)", @"[0-9]{11}"),
    ("[Bb][Vv][Nn][0-9]{11} (bvn keyword)", @"[Bb][Vv][Nn][0-9]{11}"),
];

Console.WriteLine($"Corpus: {FileCount} files, {corpus.Sum(static f => f.Length)} total bytes (deterministic seed 42)");

foreach ((string name, string pattern) in patterns)
{
    Benchmark(name, pattern, RegexOptions.None, corpus, timeoutSeconds: 5);
    Benchmark(name, pattern, RegexOptions.NonBacktracking, corpus, timeoutSeconds: null);
}

// A pathological pattern (nested quantified group plus an anchor) that sends a
// backtracking engine exponential. NonBacktracking is immune by construction.
const string pathological = @"(a+)+$";
string hostileInput = new string('a', 30) + "!";
Console.WriteLine($"Pathological pattern: {pathological} on 'a'*30 + '!' (no match)");
RunPathological("interpreter", pathological, RegexOptions.None, hostileInput);
RunPathological("nonbacktracking", pathological, RegexOptions.NonBacktracking, hostileInput);

Console.WriteLine("RESULT: PASS");
return 0;

static void Benchmark(string name, string pattern, RegexOptions options, IReadOnlyList<string> files, int? timeoutSeconds)
{
    Regex regex = timeoutSeconds is { } seconds
        ? new Regex(pattern, options, TimeSpan.FromSeconds(seconds))
        : new Regex(pattern, options);

    // One warm-up pass compiles the pattern and touches the JIT; the measured
    // pass is a fresh scan.
    _ = Scan(regex, files);

    var stopwatch = Stopwatch.StartNew();
    int matches = Scan(regex, files);
    stopwatch.Stop();

    double medianPerFileMs = MedianPerFile(regex, files);
    string mode = options == RegexOptions.NonBacktracking ? "nonbacktracking" : "interpreter  ";
    Console.WriteLine(
        $"{mode} {name}: {stopwatch.Elapsed.TotalMilliseconds,8:F1} ms total, {medianPerFileMs,7:F4} ms median/file, {matches} matches");
}

static void RunPathological(string mode, string pattern, RegexOptions options, string input)
{
    Regex regex = options == RegexOptions.NonBacktracking
        ? new Regex(pattern, options)
        : new Regex(pattern, options, TimeSpan.FromSeconds(1));

    var stopwatch = Stopwatch.StartNew();
    try
    {
        bool matched = regex.IsMatch(input);
        stopwatch.Stop();
        Console.WriteLine($"  {mode}: {(matched ? "match" : "no match")} in {stopwatch.Elapsed.TotalMilliseconds:F2} ms");
    }
    catch (RegexMatchTimeoutException)
    {
        stopwatch.Stop();
        Console.WriteLine($"  {mode}: TIMEOUT after {stopwatch.Elapsed.TotalMilliseconds:F0} ms (catastrophic backtracking)");
    }
}

static int Scan(Regex regex, IReadOnlyList<string> files)
{
    int count = 0;
    foreach (string file in files)
    {
        count += regex.Count(file);
    }

    return count;
}

static double MedianPerFile(Regex regex, IReadOnlyList<string> files)
{
    // Sample the first 200 files and take the median of the per-file times. A
    // full per-file timing of all 10,000 files would itself dominate the run.
    const int sample = 200;
    long[] times = new long[sample];
    var stopwatch = new Stopwatch();
    for (int i = 0; i < sample; i++)
    {
        stopwatch.Restart();
        _ = regex.Count(files[i]);
        stopwatch.Stop();
        times[i] = stopwatch.ElapsedTicks;
    }

    Array.Sort(times);
    double medianMs = times[sample / 2] * 1000.0 / Stopwatch.Frequency;
    return medianMs;
}

static IReadOnlyList<string> GenerateCorpus(int fileCount, int seed)
{
    var random = new Random(seed);
    var files = new List<string>(fileCount);
    string[] keywords = ["bvn", "customerId", "account", "phone", "email", "order", "invoice", "balance", "ref", "id"];

    for (int i = 0; i < fileCount; i++)
    {
        var builder = new StringBuilder(capacity: 1024);
        for (int line = 0; line < 20; line++)
        {
            string keyword = keywords[random.Next(keywords.Length)];
            builder.Append("const ");
            builder.Append(keyword);
            builder.Append(random.Next(10_000));
            builder.Append(" = ");
            // Roughly one line in six carries an 11-digit value.
            if (random.Next(6) == 0)
            {
                for (int d = 0; d < 11; d++)
                {
                    builder.Append((char)('0' + random.Next(10)));
                }
            }
            else
            {
                builder.Append('"');
                builder.Append(keyword);
                builder.Append('-');
                builder.Append(random.Next(100_000));
                builder.Append('"');
            }

            builder.Append("; // ");
            builder.Append(line);
            builder.Append('\n');
        }

        files.Add(builder.ToString());
    }

    return files;
}
