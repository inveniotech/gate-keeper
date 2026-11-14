// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Sample.AspNetCore;

/// <summary>
/// The process entry point. The sample takes the signed pack directory and the
/// installation's key catalogue; with neither, it explains what to do and exits
/// without starting.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "Usage: gatekeeper-sample-aspnetcore <pack-directory> <key-file>");
            Console.Error.WriteLine(
                "The pack directory carries the signed pack's release media files, and the key file is the " +
                "installation's public-only key catalogue.");
            return 1;
        }

        return SampleApplication.Run(args[0], args[1], Console.Out, Console.Error);
    }
}
