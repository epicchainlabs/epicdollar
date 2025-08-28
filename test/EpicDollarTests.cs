using System.Numerics;
using FluentAssertions;
using EpicChain;
using EpicChain.VM;
using NeoTestHarness;
using Xunit;
using EpicChain.Assertions;
using EpicChain.BlockchainToolkit.SmartContract;
using EpicChain.BlockchainToolkit.Models;
using EpicChain.BlockchainToolkit;
using EpicChain.SmartContract;
using EpicChain.Contracts;

namespace EpicDollarTests
{
    [CheckpointPath("checkpoints/contract-deployed.neoxp-checkpoint")]
    public class EpicDollarTests : IClassFixture<CheckpointFixture<EpicDollarTests>>
    {
        const string SYMBOL = "XUSD";
        const byte DECIMALS = 8;
        const long TOTAL_SUPPLY = 989_715_434_00000000;

        readonly CheckpointFixture fixture;
        readonly ExpressChain chain;

        public EpicDollarTests(CheckpointFixture<EpicDollarTests> fixture)
        {
            this.fixture = fixture;
            this.chain = fixture.FindChain();
        }

        [Fact]
        public void test_symbol_and_decimals()
        {
            using var snapshot = fixture.GetSnapshot();
            var contract = snapshot.GetContract<XUSDToken>();

            using var engine = new TestApplicationEngine(snapshot, ProtocolSettings.Default);
            engine.ExecuteScript<XUSDToken>(c => c.symbol(), c => c.decimals());

            engine.State.Should().Be(VMState.HALT);
            engine.ResultStack.Should().HaveCount(2);
            engine.ResultStack.Peek(0).Should().BeEquivalentTo(DECIMALS);
            engine.ResultStack.Peek(1).Should().BeEquivalentTo(SYMBOL);
        }

        [Fact]
        public void test_initial_total_supply()
        {
            using var snapshot = fixture.GetSnapshot();
            var contract = snapshot.GetContract<XUSDToken>();

            using var engine = new TestApplicationEngine(snapshot, ProtocolSettings.Default);
            engine.ExecuteScript<XUSDToken>(c => c.totalSupply());

            engine.State.Should().Be(VMState.HALT);
            engine.ResultStack.Should().HaveCount(1);
            engine.ResultStack.Peek(0).Should().BeEquivalentTo(TOTAL_SUPPLY);
        }

        [Theory]
        [InlineData("owen", TOTAL_SUPPLY)]
        [InlineData("alice", 0)]
        public void test_balances(string accountName, long amount)
        {
            var settings = chain.GetProtocolSettings();
            var account = chain.GetDefaultAccount(accountName).ToScriptHash(chain.AddressVersion);

            using var snapshot = fixture.GetSnapshot();
            var contract = snapshot.GetContract<XUSDToken>();

            using var engine = new TestApplicationEngine(snapshot, settings);
            engine.ExecuteScript<XUSDToken>(c => c.balanceOf(account));

            engine.State.Should().Be(VMState.HALT);
            engine.ResultStack.Should().HaveCount(1);
            engine.ResultStack.Peek(0).Should().BeEquivalentTo(amount);
        }

        [Fact]
        public void test_transfer()
        {
            var sender = chain.GetDefaultAccount("owen").ToScriptHash(chain.AddressVersion);
            var receiver = chain.GetDefaultAccount("alice").ToScriptHash(chain.AddressVersion);
            var amount = 1000;

            using var snapshot = fixture.GetSnapshot();
            var contract = snapshot.GetContract<XUSDToken>();

            using var engine = new TestApplicationEngine(snapshot, chain.GetProtocolSettings(), sender);
            engine.ExecuteScript<XUSDToken>(c => c.transfer(sender, receiver, amount, null));

            engine.State.Should().Be(VMState.HALT);
            engine.ResultStack.Should().HaveCount(1);
            engine.ResultStack.Peek(0).Should().BeTrue();
            engine.Notifications.Should().HaveCount(1);
            engine.Notifications[0].Should()
                .BeSentBy(contract);
                // .And
                // .BeEquivalentTo<XUSDToken.Events>(c => c.Transfer(sender, receiver, amount));
        }

        // [Fact]
        // public void test_storage()
        // {
        //     var owen = chain.GetDefaultAccount("owen").ToScriptHash(chain.AddressVersion);
        //     var alice = chain.GetDefaultAccount("alice").ToScriptHash(chain.AddressVersion);

        //     using var snapshot = fixture.GetSnapshot();

        //     var storages = snapshot.GetContractStorages<XUSDToken>();
        //     storages.Should().HaveCount(2);

        //     var assets = storages.StorageMap("asset");
        //     assets.Should().HaveCount(2);
        //     assets.TryGetValue("enable", out var enable).Should().BeTrue();
        //     enable.Should().Be(1);
        //     assets.TryGetValue(owen, out var owenBalance).Should().BeTrue();
        //     owenBalance.Should().Be(TOTAL_SUPPLY);
        //     assets.TryGetValue(alice, out var _).Should().BeFalse();

        //     var contracts = storages.StorageMap("contract");
        //     contracts.Should().HaveCount(1);
        //     contracts.TryGetValue("totalSupply", out var totalSupply).Should().BeTrue();
        //     totalSupply.Should().Be(TOTAL_SUPPLY);
        // }
    }
}
