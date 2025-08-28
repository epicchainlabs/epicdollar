using FluentAssertions;
using EpicChain.BlockchainToolkit.Models;
using EpicChain.SmartContract;
using EpicChain.VM;
using NeoTestHarness;
using Xunit;

namespace EpicChain.Contracts.Core.Tests
{
    [CheckpointPath("checkpoints/contracts-deployed.neoxp-checkpoint")]
    public class VaultTests : IClassFixture<CheckpointFixture<VaultTests>>
    {
        readonly CheckpointFixture fixture;
        readonly ExpressChain chain;

        public VaultTests(CheckpointFixture<VaultTests> fixture)
        {
            this.fixture = fixture;
            this.chain = fixture.FindChain();
        }

        [Fact]
        public void TestDeployment()
        {
            // TODO: Add deployment test
        }
    }
}
