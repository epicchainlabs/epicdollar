using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;
using EpicChain.Contracts.Interfaces;

namespace EpicChain.Contracts.Stability
{
    [DisplayName("LiquidationEngine")]
    public class LiquidationEngine : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        // Events
        public delegate void OnLiquidation(UInt160 vaultOwner, BigInteger debt, BigInteger collateral, UInt160 liquidator);
        public static event OnLiquidation onLiquidation;

        // Storage
        private static StorageMap Settings => new StorageMap(Storage.CurrentContext, "settings");
        private const string LiquidationRewardKey = "lr";

        // Vault Contract
        private static readonly UInt160 VaultAddress = (UInt160)new byte[] { /* Vault Contract Address */ };

        // Oracle Contract
        private static readonly UInt160 OracleAddress = (UInt160)new byte[] { /* Oracle Contract Address */ };

        // Stability Pool Contract
        private static readonly UInt160 StabilityPoolAddress = (UInt160)new byte[] { /* Stability Pool Contract Address */ };

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;
            var tx = (Transaction)Runtime.ScriptContainer;
            Roles.GrantRole(DEFAULT_ADMIN_ROLE, tx.Sender);
            Settings.Put(LiquidationRewardKey, 5); // 5% liquidation reward
        }

        public static void SetLiquidationReward(BigInteger reward)
        {
            Roles.RequireRole(ADMIN_ROLE, (UInt160)Runtime.CallingScriptHash);
            Settings.Put(LiquidationRewardKey, reward);
        }

        /// <summary>
        /// Liquidates an under-collateralized vault.
        /// </summary>
        /// <param name="vaultOwner">The owner of the vault to liquidate.</param>
        public static void Liquidate(UInt160 vaultOwner)
        {
            Pausable.RequireNotPaused();
            if (!vaultOwner.IsValid) throw new Exception("Invalid arguments");

            if (!IsUnderCollateralized(vaultOwner)) throw new Exception("Vault is not under-collateralized");

            BigInteger debt = (BigInteger)Contract.Call(VaultAddress, "getDebt", CallFlags.ReadOnly, vaultOwner);
            BigInteger collateral = (BigInteger)Contract.Call(VaultAddress, "getCollateral", CallFlags.ReadOnly, vaultOwner);

            // Clear the vault's debt and collateral
            if ((bool)Contract.Call(VaultAddress, "clear", CallFlags.All, vaultOwner) == false) throw new Exception("Failed to clear vault");

            BigInteger liquidationReward = (BigInteger)Settings.Get(LiquidationRewardKey);
            BigInteger rewardAmount = collateral * liquidationReward / 100;

            // Transfer reward to the liquidator
            var liquidator = (Transaction)Runtime.ScriptContainer;
            if (GAS.Transfer(Runtime.ExecutingScriptHash, liquidator.Sender, rewardAmount) == false) throw new Exception("Reward transfer failed");

            BigInteger remainingCollateral = collateral - rewardAmount;

            // Send the remaining collateral and debt to the stability pool
            Contract.Call(StabilityPoolAddress, "onLiquidation", CallFlags.All, debt, remainingCollateral);

            onLiquidation(vaultOwner, debt, collateral, liquidator.Sender);
        }

        /// <summary>
        /// Checks if a vault is under-collateralized.
        /// </summary>
        /// <param name="vaultOwner">The owner of the vault to check.</param>
        /// <returns>True if the vault is under-collateralized, false otherwise.</returns>
        [Safe]
        public static bool IsUnderCollateralized(UInt160 vaultOwner)
        {
            BigInteger debt = (BigInteger)Contract.Call(VaultAddress, "getDebt", CallFlags.ReadOnly, vaultOwner);
            if (debt == 0) return false;

            BigInteger collateral = (BigInteger)Contract.Call(VaultAddress, "getCollateral", CallFlags.ReadOnly, vaultOwner);
            BigInteger collateralPrice = (BigInteger)Contract.Call(OracleAddress, "getPrice", CallFlags.ReadOnly);
            BigInteger collateralValue = collateral * collateralPrice;

            BigInteger collateralizationRatio = (BigInteger)Contract.Call(VaultAddress, "getCollateralizationRatio", CallFlags.ReadOnly);
            BigInteger minCollateralValue = debt * collateralizationRatio / 100;

            return collateralValue < minCollateralValue;
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