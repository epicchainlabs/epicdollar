using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;

namespace EpicChain.Contracts.Governance
{
    [DisplayName("TimeLock")]
    public class TimeLock : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] PROPOSER_ROLE = "PROPOSER_ROLE";
        public static readonly byte[] EXECUTOR_ROLE = "EXECUTOR_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        // Events
        public delegate void OnTransactionQueued(byte[] txId, UInt160 target, BigInteger value, string signature, byte[] data, BigInteger eta);
        public static event OnTransactionQueued onTransactionQueued;

        public delegate void OnTransactionExecuted(byte[] txId, UInt160 target, BigInteger value, string signature, byte[] data);
        public static event OnTransactionExecuted onTransactionExecuted;

        public delegate void OnTransactionCanceled(byte[] txId);
        public static event OnTransactionCanceled onTransactionCanceled;

        // Storage
        private static StorageMap QueuedTransactions => new StorageMap(Storage.CurrentContext, "queued_transactions");
        private static StorageMap Settings => new StorageMap(Storage.CurrentContext, "settings");

        private const string DelayKey = "delay";

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;
            var tx = (Transaction)Runtime.ScriptContainer;
            Roles.GrantRole(DEFAULT_ADMIN_ROLE, tx.Sender);
            Settings.Put(DelayKey, 172800); // 2 days in seconds
        }

        public static void SetDelay(BigInteger delay)
        {
            Roles.RequireRole(ADMIN_ROLE, (UInt160)Runtime.CallingScriptHash);
            Settings.Put(DelayKey, delay);
        }

        [Safe]
        public static BigInteger GetDelay()
        {
            return (BigInteger)Settings.Get(DelayKey);
        }

        public static void QueueTransaction(UInt160 target, BigInteger value, string signature, byte[] data, BigInteger eta)
        {
            Roles.RequireRole(PROPOSER_ROLE, (UInt160)Runtime.CallingScriptHash);
            if (eta < Runtime.Time + GetDelay()) throw new Exception("ETA is too early");

            var txId = GetTxId(target, value, signature, data);
            if ((bool)QueuedTransactions.Get(txId)) throw new Exception("Transaction already queued");

            QueuedTransactions.Put(txId, eta.ToByteArray());

            onTransactionQueued(txId, target, value, signature, data, eta);
        }

        public static void ExecuteTransaction(UInt160 target, BigInteger value, string signature, byte[] data)
        {
            Roles.RequireRole(EXECUTOR_ROLE, (UInt160)Runtime.CallingScriptHash);

            var txId = GetTxId(target, value, signature, data);
            var eta = (BigInteger)QueuedTransactions.Get(txId);
            if (eta == 0) throw new Exception("Transaction not queued");
            if (Runtime.Time < eta) throw new Exception("Timelock has not expired");

            QueuedTransactions.Delete(txId);

            // Execute the transaction
            Contract.Call(target, signature, CallFlags.All, data);

            onTransactionExecuted(txId, target, value, signature, data);
        }

        public static void CancelTransaction(UInt160 target, BigInteger value, string signature, byte[] data)
        {
            Roles.RequireRole(PROPOSER_ROLE, (UInt160)Runtime.CallingScriptHash);

            var txId = GetTxId(target, value, signature, data);
            var eta = (BigInteger)QueuedTransactions.Get(txId);
            if (eta == 0) throw new Exception("Transaction not queued");

            QueuedTransactions.Delete(txId);

            onTransactionCanceled(txId);
        }

        [Safe]
        public static byte[] GetTxId(UInt160 target, BigInteger value, string signature, byte[] data)
        {
            return CryptoLib.Sha256(target.Concat(value.ToByteArray()).Concat(signature.ToByteArray()).Concat(data));
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