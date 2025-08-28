using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;

namespace EpicChain.Contracts.CrossChain
{
    [DisplayName("Bridge")]
    public class Bridge : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] RELAYER_ROLE = "RELAYER_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        // Events
        public delegate void OnLock(UInt160 user, BigInteger amount, BigInteger destinationChainId, byte[] recipient);
        public static event OnLock onLock;

        public delegate void OnUnlock(UInt160 user, BigInteger amount, BigInteger sourceChainId, byte[] recipient);
        public static event OnUnlock onUnlock;

        // Storage
        private static StorageMap LockedBalances => new StorageMap(Storage.CurrentContext, "locked_balances");

        // XUSD Token
        private static readonly UInt160 XUSDAddress = (UInt160)new byte[] { /* XUSD Token Address */ };

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;
            var tx = (Transaction)Runtime.ScriptContainer;
            Roles.GrantRole(DEFAULT_ADMIN_ROLE, tx.Sender);
        }

        /// <summary>
        /// Locks the stablecoin on the source chain.
        /// </summary>
        /// <param name="amount">The amount of stablecoin to lock.</param>
        /// <param name="destinationChainId">The ID of the destination chain.</param>
        /// <param name="recipient">The recipient address on the destination chain.</param>
        public static void Lock(BigInteger amount, BigInteger destinationChainId, byte[] recipient)
        {
            Pausable.RequireNotPaused();
            var user = (UInt160)Runtime.CallingScriptHash;
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            if ((bool)Contract.Call(XUSDAddress, "transfer", CallFlags.All, user, Runtime.ExecutingScriptHash, amount, null) == false) throw new Exception("XUSD transfer failed");

            BigInteger currentLockedBalance = (BigInteger)LockedBalances.Get(user);
            LockedBalances.Put(user, currentLockedBalance + amount);

            onLock(user, amount, destinationChainId, recipient);
        }

        /// <summary>
        /// Unlocks the stablecoin on the destination chain.
        /// </summary>
        /// <param name="amount">The amount of stablecoin to unlock.</param>
        /// <param name="sourceChainId">The ID of the source chain.</param>
        /// <param name="recipient">The recipient address on the destination chain.</param>
        /// <param name="signature">The signature from the bridge relayer.</param>
        public static void Unlock(BigInteger amount, BigInteger sourceChainId, byte[] recipient, byte[] signature)
        {
            Roles.RequireRole(RELAYER_ROLE, (UInt160)Runtime.CallingScriptHash);
            Pausable.RequireNotPaused();

            var message = amount.ToByteArray().Concat(sourceChainId.ToByteArray()).Concat(recipient);
            if (!CryptoLib.VerifyWithECDsa(message, (ECPoint)new StorageMap(Storage.CurrentContext, "relayer").Get("relayer"), signature, Curve.Secp256k1)) throw new Exception("Invalid signature");

            var user = (UInt160)recipient;
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");

            BigInteger currentLockedBalance = (BigInteger)LockedBalances.Get(user);
            if (currentLockedBalance < amount) throw new Exception("Insufficient locked balance");

            if ((bool)Contract.Call(XUSDAddress, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, user, amount, null) == false) throw new Exception("XUSD transfer failed");

            LockedBalances.Put(user, currentLockedBalance - amount);

            onUnlock(user, amount, sourceChainId, recipient);
        }

        public static void SetRelayer(ECPoint relayer)
        {
            Roles.RequireRole(ADMIN_ROLE, (UInt160)Runtime.CallingScriptHash);
            new StorageMap(Storage.CurrentContext, "relayer").Put("relayer", relayer);
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