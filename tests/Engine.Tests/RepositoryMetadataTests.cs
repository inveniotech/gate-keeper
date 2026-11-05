// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class RepositoryMetadataTests
{
    [Theory]
    [InlineData("git@github.com:Acme/payments.git", "github.com/Acme/payments")]
    [InlineData("https://user:synthetic-secret@GITHUB.com:8443/Acme/payments.git?token=synthetic#fragment", "github.com/Acme/payments")]
    [InlineData("ssh://git@github.com:22/Acme/payments.git/", "github.com/Acme/payments")]
    [InlineData("/local/repository", "")]
    [InlineData("C:/local/repository", "")]
    public void RepositoryMetadata_Should_RemoveCredentialsAndPreservePathCase_When_NormalisingRemote(string remote, string expected)
        => RepositoryMetadata.NormaliseRemote(remote).Should().Be(expected);
}
