using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;
using EpicChain.Contracts.Interfaces;

namespace EpicChain.Contracts.Treasury
{
    [DisplayName("Treasury")]
    public class Treasury : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] GOVERNANCE_ROLE = "GOVERNANCE_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        // Events
        public delegate void OnDeposit(UInt160 from, BigInteger amount);
        public static event OnDeposit onDeposit;

        public delegate void OnWithdraw(UInt160 to, BigInteger amount);
        public static event OnWithdraw onWithdraw;

        public delegate void OnRewardsDistributed(BigInteger amount);
        public static event OnRewardsDistributed onRewardsDistributed;

        // Storage
        private static StorageMap Balance => new StorageMap(Storage.CurrentContext, "balance");

        // Staking Contract
        private static readonly UInt160 StakingAddress = (UInt160)new byte[] { /* Staking Contract Address */ };

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;
            var tx = (Transaction)Runtime.ScriptContainer;
            Roles.GrantRole(DEFAULT_ADMIN_ROLE, tx.Sender);
        }

        /// <summary>
        /// Deposits funds into the treasury.
        /// </summary>
        /// <param name="from">The address depositing funds.</param>
        /// <param name="amount">The amount of funds to deposit.</param>
        public static void Deposit(UInt160 from, BigInteger amount)
        {
            Pausable.RequireNotPaused();
            if (!from.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(from)) throw new Exception("No witness");

            // For now, we assume the deposited asset is GAS.
            if (GAS.Transfer(from, Runtime.ExecutingScriptHash, amount) == false) throw new Exception("GAS transfer failed");

            BigInteger currentBalance = (BigInteger)Balance.Get("total");
            Balance.Put("total", currentBalance + amount);

            onDeposit(from, amount);
        }

        /// <summary>
        /// Withdraws funds from the treasury.
        /// </summary>
        /// <param name="to">The address to withdraw funds to.</param>
        /// <param name="amount">The amount of funds to withdraw.</param>
        public static void Withdraw(UInt160 to, BigInteger amount)
        {
            Roles.RequireRole(GOVERNANCE_ROLE, (UInt160)Runtime.CallingScriptHash);
            Pausable.RequireNotPaused();
            if (!to.IsValid || amount <= 0) throw new Exception("Invalid arguments");

            BigInteger currentBalance = (BigInteger)Balance.Get("total");
            if (currentBalance < amount) throw new Exception("Insufficient funds");

            if (GAS.Transfer(Runtime.ExecutingScriptHash, to, amount) == false) throw new Exception("GAS transfer failed");

            Balance.Put("total", currentBalance - amount);

            onWithdraw(to, amount);
        }

        /// <summary>
        /// Distributes funds from the treasury to the staking contract.
        /// </summary>
        /// <param name="amount">The amount of funds to distribute.</param>
        public static void DistributeToStakers(BigInteger amount)
        {
            Roles.RequireRole(GOVERNANCE_ROLE, (UInt160)Runtime.CallingScriptHash);
            Pausable.RequireNotPaused();
            if (amount <= 0) throw new Exception("Invalid amount");

            BigInteger currentBalance = (BigInteger)Balance.Get("total");
            if (currentBalance < amount) throw new Exception("Insufficient funds");

            if ((bool)Contract.Call(StakingAddress, "distributeRewards", CallFlags.All, amount) == false) throw new Exception("Reward distribution failed");

            Balance.Put("total", currentBalance - amount);

            onRewardsDistributed(amount);
        }

        public static void GrantRole(byte[] role, UInt160 member)
        {
            Roles.RequireRole(ADMIN_ROLE, (UInt160)Runtime.CallingScriptHash);
            Roles.GrantRole(role, member);
        }

        public static void RevokeRole(byte[] role, UInt160 member)
        {
            Roles.RequireRole(ADMIN_ROLE, (UInt160)Runtime.CallingScriptHash);
            Roles.RevokeRole(role, member);
        }
    }
}