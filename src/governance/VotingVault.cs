using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;

namespace EpicChain.Contracts.Governance
{
    [DisplayName("VotingVault")]
    public class VotingVault : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        // Events
        public delegate void OnDeposit(UInt160 user, BigInteger amount);
        public static event OnDeposit onDeposit;

        public delegate void OnWithdraw(UInt160 user, BigInteger amount);
        public static event OnWithdraw onWithdraw;

        // Storage
        private static StorageMap Balances => new StorageMap(Storage.CurrentContext, "balances");
        private static StorageMap TotalBalance => new StorageMap(Storage.CurrentContext, "total_balance");

        // Governance Token
        private static readonly UInt160 GovernanceTokenAddress = (UInt160)new byte[] { /* Governance Token Address */ };

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;
            var tx = (Transaction)Runtime.ScriptContainer;
            Roles.GrantRole(DEFAULT_ADMIN_ROLE, tx.Sender);
        }

        /// <summary>
        /// Deposits governance tokens into the voting vault.
        /// </summary>
        /// <param name="user">The user depositing tokens.</param>
        /// <param name="amount">The amount of tokens to deposit.</param>
        public static void Deposit(UInt160 user, BigInteger amount)
        {
            Pausable.RequireNotPaused();
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            if ((bool)Contract.Call(GovernanceTokenAddress, "transfer", CallFlags.All, user, Runtime.ExecutingScriptHash, amount, null) == false) throw new Exception("Governance token transfer failed");

            BigInteger currentBalance = (BigInteger)Balances.Get(user);
            Balances.Put(user, currentBalance + amount);

            BigInteger totalBalance = (BigInteger)TotalBalance.Get("total");
            TotalBalance.Put("total", totalBalance + amount);

            onDeposit(user, amount);
        }

        /// <summary>
        /// Withdraws governance tokens from the voting vault.
        /// </summary>
        /// <param name="user">The user withdrawing tokens.</param>
        /// <param name="amount">The amount of tokens to withdraw.</param>
        public static void Withdraw(UInt160 user, BigInteger amount)
        {
            Pausable.RequireNotPaused();
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            BigInteger currentBalance = (BigInteger)Balances.Get(user);
            if (currentBalance < amount) throw new Exception("Insufficient balance");

            if ((bool)Contract.Call(GovernanceTokenAddress, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, user, amount, null) == false) throw new Exception("Governance token transfer failed");

            Balances.Put(user, currentBalance - amount);

            BigInteger totalBalance = (BigInteger)TotalBalance.Get("total");
            TotalBalance.Put("total", totalBalance - amount);

            onWithdraw(user, amount);
        }

        [Safe]
        public static BigInteger GetVotes(UInt160 account)
        {
            return (BigInteger)Balances.Get(account);
        }

        [Safe]
        public static BigInteger GetTotalVotes()
        {
            return (BigInteger)TotalBalance.Get("total");
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