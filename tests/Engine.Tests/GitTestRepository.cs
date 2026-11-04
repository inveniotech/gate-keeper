// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// A throw-away git repository under the machine's temporary directory, built
/// file by file with fixed author identity and fixed commit dates so the test
/// runs are deterministic (rule 26). Commits carry no random data, so a given
/// repository produces the same blob and commit object identifiers on every
/// machine that runs the tests.
/// </summary>
public sealed class GitTestRepository
{
    private const string TestTimeStamp = "2026-01-05T09:30:00+00:00";
    private const string TestName = "Gate-Keeper Corpus Builder";
    private const string TestEmail = "corpus@gatekeeper.africa";

    private const string AutocrlfKey = "core.autocrlf";
    private const string AutocrlfValue = "false";
    private const string UserEmailConfigKey = "user.email";
    private const string UserEmailConfigValue = TestEmail;
    private const string UserNameConfigKey = "user.name";
    private const string UserNameConfigValue = TestName;

    private const string HumidityHeading = "Humidity sensor calibration log";

    private GitTestRepository(string rootPath)
    {
        RootPath = rootPath;
    }

    /// <summary>Gets the absolute path of the repository's root directory.</summary>
    public string RootPath { get; }

    /// <summary>
    /// Creates an empty git repository with the identity and the line-ending
    /// settings the tests rely on.
    /// </summary>
    /// <returns>The repository.</returns>
    public static GitTestRepository Create()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "gatekeeper-tests",
            $"gk-{Guid.NewGuid():N}");

        Directory.CreateDirectory(rootPath);
        Run(rootPath, "init", "--quiet", "-b", "master");
        Run(rootPath, "config", AutocrlfKey, AutocrlfValue);
        Run(rootPath, "config", UserEmailConfigKey, UserEmailConfigValue);
        Run(rootPath, "config", UserNameConfigKey, UserNameConfigValue);

        return new GitTestRepository(rootPath);
    }

    /// <summary>
    /// Creates a plain directory (no git), for the outside-git tests.
    /// </summary>
    /// <returns>A repository wrapper whose root holds no git repository.</returns>
    public static GitTestRepository CreatePlainDirectory()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "gatekeeper-tests",
            $"gk-{Guid.NewGuid():N}");

        Directory.CreateDirectory(rootPath);
        return new GitTestRepository(rootPath);
    }

    /// <summary>
    /// Writes a text file under the repository root with LF line endings and
    /// no byte-order mark, creating the folders along the path (rule 22).
    /// </summary>
    /// <param name="relativePath">The forward-slash path relative to the root.</param>
    /// <param name="content">The text to write.</param>
    public void WriteFile(string relativePath, string content)
    {
        var fullPath = FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Writes a file's exact bytes, for binary and encoding fixtures.</summary>
    /// <param name="relativePath">The forward-slash path relative to the root.</param>
    /// <param name="bytes">The bytes to write.</param>
    public void WriteBytes(string relativePath, byte[] bytes)
    {
        var fullPath = FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }

    /// <summary>Stages every path given, as <c>git add</c>.</summary>
    /// <param name="relativePaths">Forward-slash paths relative to the root.</param>
    public void Stage(params string[] relativePaths)
    {
        Run(RootPath, relativePaths.Length == 0
            ? ["add", "--", "."]
            : ["add", "--", .. relativePaths]);
    }

    /// <summary>
    /// Commits the current index with a fixed identity, a fixed date and the
    /// given message, so the object identifiers are deterministic.
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <returns>The full object identifier of the new commit.</returns>
    public string Commit(string message)
    {
        Run(RootPath, "commit", $"--no-gpg-sign", "-m", message);
        return Run(RootPath, "rev-parse", "HEAD").TrimEnd('\n');
    }

    /// <summary>
    /// Clones the given repository one commit deep with a fixed file protocol
    /// and line-ending configuration, so the clone is byte-identical on every
    /// machine (the shallow fixture for the history warning).
    /// </summary>
    /// <param name="source">The repository to clone.</param>
    /// <returns>The shallow clone.</returns>
    public static GitTestRepository ShallowClone(GitTestRepository source)
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "gatekeeper-tests",
            $"gk-{Guid.NewGuid():N}");

        Run(
            Path.GetTempPath(),
            "-c",
            "protocol.file.allow=user",
            "-c",
            AutocrlfKey + "=" + AutocrlfValue,
            "-c",
            "core.eol=lf",
            "clone",
            "--depth",
            "1",
            new Uri(source.RootPath).AbsoluteUri,
            rootPath);

        return new GitTestRepository(rootPath);
    }

    /// <summary>Runs git inside this repository and returns its standard output text.</summary>
    /// <param name="arguments">The git arguments, without the executable name.</param>
    /// <returns>The standard output.</returns>
    public string Git(params string[] arguments) => Run(RootPath, arguments);

    /// <summary>The absolute path of a repository-relative path.</summary>
    /// <param name="relativePath">The forward-slash path relative to the root.</param>
    /// <returns>The absolute path.</returns>
    public string FullPath(string relativePath) => Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Builds the enumeration options for this repository.</summary>
    /// <param name="configure">Optional adjustments to the defaults.</param>
    /// <returns>The options pointing at this repository's root.</returns>
    public FileEnumerationOptions Options(Action<FileEnumerationOptions>? configure = null)
    {
        var options = new FileEnumerationOptions
        {
            Root = RootPath,
        };
        configure?.Invoke(options);
        return options;
    }

    private static string Run(string directory, params string[] arguments)
    {
        var argumentsLine = string.Join(
            ' ',
            arguments.Select(argument => argument.Contains(' ', StringComparison.Ordinal)
                ? '"' + argument + '"'
                : argument));

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = argumentsLine,
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // The identity environment makes test commits byte-identical on every
        // machine and every run (rule 26).
        startInfo.Environment["GIT_AUTHOR_NAME"] = TestName;
        startInfo.Environment["GIT_AUTHOR_EMAIL"] = TestEmail;
        startInfo.Environment["GIT_AUTHOR_DATE"] = TestTimeStamp;
        startInfo.Environment["GIT_COMMITTER_NAME"] = TestName;
        startInfo.Environment["GIT_COMMITTER_EMAIL"] = TestEmail;
        startInfo.Environment["GIT_COMMITTER_DATE"] = TestTimeStamp;

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {argumentsLine} inside '{directory}' failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }

    /// <summary>Synthetic log line for the calibration fixture (NFR-004).</summary>
    /// <param name="ordinal">The one-based ordinal inside the fixture.</param>
    /// <returns>A synthetic, human-readable line.</returns>
    public static string SensorLine(int ordinal) =>
        $"{HumidityHeading} entry {ordinal:D4}";
}


