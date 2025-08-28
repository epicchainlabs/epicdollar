using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;

namespace EpicChain.Contracts.AMM
{
    [DisplayName("EpicSwapFactory")]
    public class EpicSwapFactory : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        // Events
        public delegate void OnPairCreated(UInt160 token0, UInt160 token1, UInt160 pair, BigInteger allPairsLength);
        public static event OnPairCreated onPairCreated;

        // Storage
        private static StorageMap Pairs => new StorageMap(Storage.CurrentContext, "pairs");
        private static StorageMap AllPairs => new StorageMap(Storage.CurrentContext, "all_pairs");

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;
            var tx = (Transaction)Runtime.ScriptContainer;
            Roles.GrantRole(DEFAULT_ADMIN_ROLE, tx.Sender);
        }

        /// <summary>
        /// Creates a new trading pair for the given tokens.
        /// </summary>
        /// <param name="tokenA">The address of the first token.</param>
        /// <param name="tokenB">The address of the second token.</param>
        /// <returns>The address of the new trading pair.</returns>
        public static UInt160 CreatePair(UInt160 tokenA, UInt160 tokenB)
        {
            if (tokenA == tokenB) throw new Exception("Identical tokens");
            var (token0, token1) = tokenA.ToBigInteger() < tokenB.ToBigInteger() ? (tokenA, tokenB) : (tokenB, tokenA);

            if (GetPair(token0, token1) != null) throw new Exception("Pair already exists");

            // This is a simplified version of pair creation. A real implementation would deploy a new contract.
            var pair = (UInt160)CryptoLib.Sha256(token0.Concat(token1));

            Pairs.Put(token0.Concat(token1), pair);
            var allPairsLength = (BigInteger)AllPairs.Get("length");
            AllPairs.Put(allPairsLength.ToByteArray(), pair);
            AllPairs.Put("length", allPairsLength + 1);

            onPairCreated(token0, token1, pair, allPairsLength + 1);

            return pair;
        }

        [Safe]
        public static UInt160 GetPair(UInt160 tokenA, UInt160 tokenB)
        {
            var (token0, token1) = tokenA.ToBigInteger() < tokenB.ToBigInteger() ? (tokenA, tokenB) : (tokenB, tokenA);
            return (UInt160)Pairs.Get(token0.Concat(token1));
        }

        [Safe]
        public static UInt160 AllPairs(BigInteger index)
        {
            return (UInt160)AllPairs.Get(index.ToByteArray());
        }

        [Safe]
        public static BigInteger AllPairsLength()
        {
            return (BigInteger)AllPairs.Get("length");
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
