// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The one place the engine launches the <c>git</c> executable. The command
/// runs against an existing directory tree with the arguments given; it
/// touches no network and keeps every byte it reads inside this process
/// (rule 5). When the executable is missing, the callers degrade to the
/// directory walk, as the module's README documents.
/// </summary>
internal sealed class GitRunner
{
    /// <summary>
    /// One completed git invocation.
    /// </summary>
    /// <param name="ExitCode">The process exit code, 0 on success.</param>
    /// <param name="StandardOutput">The standard output's bytes.</param>
    /// <param name="StandardError">The standard error's text.</param>
    internal readonly record struct GitResult(int ExitCode, byte[] StandardOutput, string StandardError);

    /// <summary>
    /// Runs git with the given arguments inside the given directory. A
    /// missing <c>git</c> executable yields <see langword="null"/>; every
    /// other completion yields the exit code and both streams.
    /// </summary>
    /// <param name="directory">The working directory of the invocation.</param>
    /// <param name="arguments">The git arguments, without the executable name.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>The result, or <see langword="null"/> when git could not start.</returns>
    internal async Task<GitResult?> TryRunAsync(
        string directory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var process = StartProcess(directory, arguments);

        if (process is null)
        {
            return null;
        }

        var outputTask = ReadFullyAsync(process.StandardOutput.BaseStream, cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new GitResult(process.ExitCode, output, error);
    }

    private static Process? StartProcess(string directory, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = directory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is Win32Exception or PlatformNotSupportedException)
        {
            // 'git' is not an installed executable on this machine. The
            // callers fall back to the directory walk and record why.
            return null;
        }
    }

    /// <summary>
    /// Starts a long-lived git process whose standard input stays open, for
    /// the <c>cat-file --batch</c> protocol. The caller owns the returned
    /// process and closes it in <see cref="GitBatchReader.DisposeAsync"/>.
    /// </summary>
    /// <param name="directory">The working directory of the invocation.</param>
    /// <param name="arguments">The git arguments, without the executable name.</param>
    /// <returns>The running process, or <see langword="null"/> when git could not start.</returns>
    internal Process? StartStreaming(string directory, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = directory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is Win32Exception or PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs <c>git rev-parse --show-toplevel</c> inside the directory and
    /// gives its work-tree root, or <see langword="null"/> when the directory
    /// is not inside a work tree (or git is missing entirely, which the
    /// caller treats the same way and notes).
    /// </summary>
    /// <param name="directory">The directory the caller asked the scan to run on.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>The work-tree root path, or <see langword="null"/> when there is none.</returns>
    internal async Task<string?> FindWorkTreeRootAsync(string directory, CancellationToken cancellationToken)
    {
        var probe = await this.TryRunAsync(directory, ["rev-parse", "--show-toplevel"], cancellationToken)
            .ConfigureAwait(false);

        if (probe is not { ExitCode: 0 })
        {
            return null;
        }

        var toplevel = Encoding.UTF8.GetString(probe.Value.StandardOutput).Trim();

        return toplevel.Length == 0 ? null : toplevel;
    }

    private static async Task<byte[]> ReadFullyAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();

        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}

/// <summary>
/// A long-lived <c>git cat-file --batch</c> session: each request names one
/// object, and each answer carries the header (object id, type, size),
/// the content bytes and a blank line. The reader requests one object at a
/// time in caller-given order, so the stream stays deterministic, and it
/// discards content the caller cannot keep (too large) without building it.
/// The reader never asks git to fetch anything: the objects already sit in
/// the object database (IQ-21, rule 5).
/// </summary>
internal sealed class GitBatchReader : IAsyncDisposable
{
    private const int HeaderLineLimit = 512;
    private const int DrainChunkSize = 40_960;

    private readonly Process process;
    private readonly TextReader? reader;
    private readonly Stream input;
    private readonly Stream output;

    private GitBatchReader(Process process, Stream input, Stream output, TextReader? reader)
    {
        this.process = process;
        this.input = input;
        this.output = output;
        this.reader = reader;
    }

    /// <summary>
    /// One answered request.
    /// </summary>
    /// <param name="Type">The object type git named, for instance blob.</param>
    /// <param name="Size">The content byte length.</param>
    /// <param name="Bytes">The content bytes, when they were kept; <see langword="null"/> when the caller's limit was exceeded or the object is not a blob.</param>
    internal readonly record struct BatchObject(string Type, long Size, byte[]? Bytes);

    /// <summary>
    /// Starts the batch reader for the given repository.
    /// </summary>
    /// <param name="runner">The git runner.</param>
    /// <param name="directory">The working directory of the invocation.</param>
    /// <returns>The reader, or <see langword="null"/> when git is missing.</returns>
    internal static GitBatchReader? Start(GitRunner runner, string directory)
    {
        var process = runner.StartStreaming(directory, ["cat-file", "--batch"]);

        if (process is null)
        {
            return null;
        }

        return new GitBatchReader(
            process,
            process.StandardInput.BaseStream,
            process.StandardOutput.BaseStream,
            process.StandardOutput);
    }

    /// <summary>
    /// Asks the object database for one object and reads the answer.
    /// </summary>
    /// <param name="objectId">The 40-character lowercase hexadecimal object identifier.</param>
    /// <param name="keepLimit">The largest content the reader builds in memory.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>The answered object, or <see langword="null"/> when the conversation broke.</returns>
    internal async Task<BatchObject?> ReadObjectAsync(
        string objectId,
        long keepLimit,
        CancellationToken cancellationToken)
    {
        var encoded = Encoding.ASCII.GetBytes(objectId + "\n");

        try
        {
            await this.input.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
            await this.input.FlushAsync(cancellationToken).ConfigureAwait(false);

            var header = await this.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (header is null)
            {
                return null;
            }

            var parts = header.Split(' ');

            if (parts.Length < 2 || parts[^1] == "missing")
            {
                // A dangler such as a missing object never happens for a
                // listing from rev-list, but the honest answer is the null
                // return and the scan keeps going.
                return null;
            }

            if (parts.Length != 3 || !long.TryParse(
                    parts[2],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var size))
            {
                return null;
            }

            var type = parts[1];

            if (type != "blob")
            {
                await this.DrainAsync(size, cancellationToken).ConfigureAwait(false);
                return new BatchObject(type, size, Bytes: null);
            }

            if (size > keepLimit)
            {
                await this.DrainAsync(size, cancellationToken).ConfigureAwait(false);
                return new BatchObject(type, size, Bytes: null);
            }

            var bytes = new byte[size];
            var read = 0;

            while (read < size)
            {
                var count = await this.output
                    .ReadAsync(bytes.AsMemory(read), cancellationToken)
                    .ConfigureAwait(false);

                if (count == 0)
                {
                    return null;
                }

                read += count;
            }

            if (await this.ReadOneAsync(cancellationToken).ConfigureAwait(false) < 0)
            {
                return null;
            }

            return new BatchObject(type, size, bytes);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            this.input.Close();
            await this.process.WaitForExitAsync(CancellationToken.None)
                .ConfigureAwait(ConfigureAwaitOptions.None);
        }
        catch (InvalidOperationException)
        {
            // The process left on its own, for instance short history.
        }

        this.process.Dispose();
    }

    private async Task<int> ReadOneAsync(CancellationToken cancellationToken)
    {
        var single = new byte[1];
        var read = await this.output.ReadAsync(single, cancellationToken).ConfigureAwait(false);
        return read == 0 ? -1 : single[0];
    }

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        var count = 0;

        while (count < HeaderLineLimit)
        {
            var value = await this.ReadOneAsync(cancellationToken).ConfigureAwait(false);

            if (value < 0)
            {
                return null;
            }

            if (value == (byte)'\n')
            {
                return line.ToString();
            }

            line.Append((char)value);
            count++;
        }

        return null;
    }

    private async Task DrainAsync(long size, CancellationToken cancellationToken)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(DrainChunkSize);

        try
        {
            var remaining = size;

            while (remaining > 0)
            {
                var count = (int)Math.Min(remaining, buffer.Length);
                var read = await this.output.ReadAsync(buffer.AsMemory(0, count), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    return;
                }

                remaining -= read;
            }

            if (await this.ReadOneAsync(cancellationToken).ConfigureAwait(false) < 0)
            {
                return;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}


