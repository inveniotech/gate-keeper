// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;

namespace InvenioTech.GateKeeper.Tests;

public sealed class EvidenceSinkExampleTests
{
    [Fact]
    public void Worked_sidecar_digest_is_the_sha256_of_the_example_bytes()
    {
        var bytes = Encoding.ASCII.GetBytes("example\n");
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        digest.Should().Be("13550350a8681c84c861aac2e5b440161c2b33a3e4f302ac680ca5b686de48de");
    }
}
