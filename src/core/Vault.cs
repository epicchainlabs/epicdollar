using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;
using EpicChain.Contracts.Interfaces;

namespace EpicChain.Contracts.Core
{
    [DisplayName("EpicVault")]
    public class Vault : ReentrancyGuard
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        // Events
        public delegate void OnDeposit(UInt160 user, BigInteger amount);
        public static event OnDeposit onDeposit;

        public delegate void OnWithdraw(UInt160 user, BigInteger amount);
        public static event OnWithdraw onWithdraw;

        public delegate void OnMint(UInt160 user, BigInteger amount);
        public static event OnMint onMint;

        public delegate void OnRepay(UInt160 user, BigInteger amount);
        public static event OnRepay onRepay;

        // Storage
        private static StorageMap Collateral => new StorageMap(Storage.CurrentContext, "collateral");
        private static StorageMap Debt => new StorageMap(Storage.CurrentContext, "debt");
        private static StorageMap Settings => new StorageMap(Storage.CurrentContext, "settings");

        private const string CollateralizationRatioKey = "cr");

        // Oracle
        private static readonly UInt160 OracleAddress = (UInt160)new byte[] { /* Oracle Address */ };

        // XUSD Token
        private static readonly UInt160 XUSDAddress = (UInt160)new byte[] { /* XUSD Token Address */ };

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;
            var tx = (Transaction)Runtime.ScriptContainer;
            Roles.GrantRole(DEFAULT_ADMIN_ROLE, tx.Sender);
            Roles.GrantRole(Pausable.PAUSER_ROLE, tx.Sender);
            Settings.Put(CollateralizationRatioKey, 150); // 150%
        }

        public static void SetCollateralizationRatio(BigInteger ratio)
        {
            Roles.RequireRole(ADMIN_ROLE, (UInt160)Runtime.CallingScriptHash);
            Settings.Put(CollateralizationRatioKey, ratio);
        }

        public static void Deposit(UInt160 user, BigInteger amount)
        {
            Pausable.RequireNotPaused();
            Enter();
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            if (GAS.Transfer(user, Runtime.ExecutingScriptHash, amount) == false) throw new Exception("GAS transfer failed");

            BigInteger currentBalance = (BigInteger)Collateral.Get(user);
            Collateral.Put(user, currentBalance + amount);

            onDeposit(user, amount);
            Leave();
        }

        public static void Withdraw(UInt160 user, BigInteger amount)
        {
            Pausable.RequireNotPaused();
            Enter();
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            BigInteger currentBalance = (BigInteger)Collateral.Get(user);
            if (currentBalance < amount) throw new Exception("Insufficient collateral");

            BigInteger currentDebt = (BigInteger)Debt.Get(user);
            if (currentDebt > 0) throw new Exception("Cannot withdraw with outstanding debt");

            if (GAS.Transfer(Runtime.ExecutingScriptHash, user, amount) == false) throw new Exception("GAS transfer failed");

            Collateral.Put(user, currentBalance - amount);

            onWithdraw(user, amount);
            Leave();
        }

        public static void Mint(UInt160 user, BigInteger amount)
        {
            Pausable.RequireNotPaused();
            Enter();
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            BigInteger collateralBalance = (BigInteger)Collateral.Get(user);
            if (collateralBalance == 0) throw new Exception("No collateral");

            BigInteger collateralPrice = (BigInteger)Contract.Call(OracleAddress, "getPrice", CallFlags.ReadOnly);
            BigInteger collateralValue = collateralBalance * collateralPrice;

            BigInteger collateralizationRatio = (BigInteger)Settings.Get(CollateralizationRatioKey);
            BigInteger maxDebt = collateralValue * 100 / collateralizationRatio;

            BigInteger currentDebt = (BigInteger)Debt.Get(user);
            if (currentDebt + amount > maxDebt) throw new Exception("Exceeds max debt");

            Debt.Put(user, currentDebt + amount);

            if ((bool)Contract.Call(XUSDAddress, "mint", CallFlags.All, user, amount) == false) throw new Exception("Minting failed");

            onMint(user, amount);
            Leave();
        }

        public static void Repay(UInt160 user, BigInteger amount)
        {
            Pausable.RequireNotPaused();
            Enter();
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            BigInteger currentDebt = (BigInteger)Debt.Get(user);
            if (amount > currentDebt) amount = currentDebt;

            if ((bool)Contract.Call(XUSDAddress, "transfer", CallFlags.All, user, Runtime.ExecutingScriptHash, amount, null) == false) throw new Exception("XUSD transfer failed");

            Debt.Put(user, currentDebt - amount);

            if ((bool)Contract.Call(XUSDAddress, "burn", CallFlags.All, Runtime.ExecutingScriptHash, amount) == false) throw new Exception("Burning failed");

            onRepay(user, amount);
            Leave();
        }

        public static void PauseContract()
        {
            Roles.RequireRole(Pausable.PAUSER_ROLE, (UInt160)Runtime.CallingScriptHash);
            Pausable.Pause();
        }

        public static void UnpauseContract()
        {
            Roles.RequireRole(Pausable.PAUSER_ROLE, (UInt160)Runtime.CallingScriptHash);
            Pausable.Unpause();
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
