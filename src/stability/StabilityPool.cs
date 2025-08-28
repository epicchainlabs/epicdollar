using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;
using EpicChain.Contracts.Interfaces;

namespace EpicChain.Contracts.Stability
{
    [DisplayName("StabilityPool")]
    public class StabilityPool : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] LIQUIDATION_ENGINE_ROLE = "LIQUIDATION_ENGINE_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        // Events
        public delegate void OnDeposit(UInt160 user, BigInteger amount);
        public static event OnDeposit onDeposit;

        public delegate void OnWithdraw(UInt160 user, BigInteger amount);
        public static event OnWithdraw onWithdraw;

        public delegate void OnRewardsClaimed(UInt160 user, BigInteger collateralReward, BigInteger tokenReward);
        public static event OnRewardsClaimed onRewardsClaimed;

        // Storage
        private static StorageMap Deposits => new StorageMap(Storage.CurrentContext, "deposits");
        private static StorageMap CollateralRewards => new StorageMap(Storage.CurrentContext, "collateral_rewards");
        private static StorageMap TokenRewards => new StorageMap(Storage.CurrentContext, "token_rewards");
        private static StorageMap TotalDepositsMap => new StorageMap(Storage.CurrentContext, "total_deposits");

        // XUSD Token
        private static readonly UInt160 XUSDAddress = (UInt160)new byte[] { /* XUSD Token Address */ };

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
        /// Deposits XUSD into the stability pool.
        /// </summary>
        /// <param name="user">The user depositing XUSD.</param>
        /// <param name="amount">The amount of XUSD to deposit.</param>
        public static void Deposit(UInt160 user, BigInteger amount)
        {
            Pausable.RequireNotPaused();
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            if ((bool)Contract.Call(XUSDAddress, "transfer", CallFlags.All, user, Runtime.ExecutingScriptHash, amount, null) == false) throw new Exception("XUSD transfer failed");

            BigInteger currentDeposit = (BigInteger)Deposits.Get(user);
            Deposits.Put(user, currentDeposit + amount);

            BigInteger totalDeposits = (BigInteger)TotalDepositsMap.Get("total");
            TotalDepositsMap.Put("total", totalDeposits + amount);

            onDeposit(user, amount);
        }

        /// <summary>
        /// Withdraws XUSD from the stability pool.
        /// </summary>
        /// <param name="user">The user withdrawing XUSD.</param>
        /// <param name="amount">The amount of XUSD to withdraw.</param>
        public static void Withdraw(UInt160 user, BigInteger amount)
        {
            Pausable.RequireNotPaused();
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            BigInteger currentDeposit = (BigInteger)Deposits.Get(user);
            if (currentDeposit < amount) throw new Exception("Insufficient deposit");

            if ((bool)Contract.Call(XUSDAddress, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, user, amount, null) == false) throw new Exception("XUSD transfer failed");

            Deposits.Put(user, currentDeposit - amount);

            BigInteger totalDeposits = (BigInteger)TotalDepositsMap.Get("total");
            TotalDepositsMap.Put("total", totalDeposits - amount);

            onWithdraw(user, amount);
        }

        /// <summary>
        /// Called by the liquidation engine to offset debt with the stability pool.
        /// </summary>
        /// <param name="debt">The amount of debt to offset.</param>
        /// <param name="collateral">The collateral from the liquidated vault.</param>
        public static void OnLiquidation(BigInteger debt, BigInteger collateral)
        {
            Roles.RequireRole(LIQUIDATION_ENGINE_ROLE, (UInt160)Runtime.CallingScriptHash);

            BigInteger totalDeposits = (BigInteger)TotalDepositsMap.Get("total");
            if (totalDeposits < debt) throw new Exception("Not enough XUSD in the stability pool");

            // Burn the debt from the stability pool
            if ((bool)Contract.Call(XUSDAddress, "burn", CallFlags.All, Runtime.ExecutingScriptHash, debt) == false) throw new Exception("Burning failed");

            // Distribute collateral to depositors
            // This is a simplified distribution model. A real implementation would be more complex.
            var depositors = Deposits.Find(FindOptions.KeysOnly | FindOptions.RemovePrefix);
            while (depositors.Next())
            {
                var depositor = (UInt160)depositors.Value;
                BigInteger depositorBalance = (BigInteger)Deposits.Get(depositor);
                BigInteger collateralShare = collateral * depositorBalance / totalDeposits;
                BigInteger currentCollateralReward = (BigInteger)CollateralRewards.Get(depositor);
                CollateralRewards.Put(depositor, currentCollateralReward + collateralShare);
            }

            TotalDepositsMap.Put("total", totalDeposits - debt);
        }

        /// <summary>
        /// Claims the user's rewards (liquidated collateral and governance tokens).
        /// </summary>
        /// <param name="user">The user claiming rewards.</param>
        public static void ClaimRewards(UInt160 user)
        {
            if (!user.IsValid) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            BigInteger collateralReward = (BigInteger)CollateralRewards.Get(user);
            if (collateralReward > 0)
            {
                CollateralRewards.Delete(user);
                // Transfer collateral to the user (assuming GAS for now)
                if (GAS.Transfer(Runtime.ExecutingScriptHash, user, collateralReward) == false) throw new Exception("Collateral transfer failed");
            }

            BigInteger tokenReward = (BigInteger)TokenRewards.Get(user);
            if (tokenReward > 0)
            {
                TokenRewards.Delete(user);
                if ((bool)Contract.Call(StakingAddress, "claimReward", CallFlags.All, user, tokenReward) == false) throw new Exception("Token reward claim failed");
            }

            onRewardsClaimed(user, collateralReward, tokenReward);
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