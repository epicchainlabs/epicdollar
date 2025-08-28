using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;

namespace EpicChain.Contracts.AMM
{
    [DisplayName("EpicSwapPair")]
    public class EpicSwapPair : SmartContract
    {
        // Events
        public delegate void OnMint(UInt160 sender, BigInteger amount0, BigInteger amount1);
        public static event OnMint onMint;

        public delegate void OnBurn(UInt160 sender, BigInteger amount0, BigInteger amount1, UInt160 to);
        public static event OnBurn onBurn;

        public delegate void OnSwap(UInt160 sender, BigInteger amount0In, BigInteger amount1In, BigInteger amount0Out, BigInteger amount1Out, UInt160 to);
        public static event OnSwap onSwap;

        public delegate void OnSync(BigInteger reserve0, BigInteger reserve1);
        public static event OnSync onSync;

        // Storage
        private static StorageMap Factory => new StorageMap(Storage.CurrentContext, "factory");
        private static StorageMap Token0 => new StorageMap(Storage.CurrentContext, "token0");
        private static StorageMap Token1 => new StorageMap(Storage.CurrentContext, "token1");
        private static StorageMap Reserves => new StorageMap(Storage.CurrentContext, "reserves");
        private static StorageMap TotalSupply => new StorageMap(Storage.CurrentContext, "total_supply");
        private static StorageMap Balances => new StorageMap(Storage.CurrentContext, "balances");

        private const ulong MINIMUM_LIQUIDITY = 1000;

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;
            var tx = (Transaction)Runtime.ScriptContainer;
            Factory.Put("factory", tx.Sender);
        }

        public static void Initialize(UInt160 token0, UInt160 token1)
        {
            if ((UInt160)Factory.Get("factory") != (UInt160)Runtime.CallingScriptHash) throw new Exception("Not factory");
            Token0.Put("token0", token0);
            Token1.Put("token1", token1);
        }

        public static void Mint(UInt160 to)
        {
            var reserves = GetReserves();
            var balance0 = (BigInteger)Contract.Call((UInt160)Token0.Get("token0"), "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash);
            var balance1 = (BigInteger)Contract.Call((UInt160)Token1.Get("token1"), "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash);
            var amount0 = balance0 - reserves.reserve0;
            var amount1 = balance1 - reserves.reserve1;

            var totalSupply = (BigInteger)TotalSupply.Get("total");
            BigInteger liquidity;
            if (totalSupply == 0)
            {
                liquidity = (BigInteger)CryptoLib.Sqrt(amount0 * amount1) - MINIMUM_LIQUIDITY;
                Mint(UInt160.Zero, MINIMUM_LIQUIDITY);
            }
            else
            {
                liquidity = (BigInteger)Math.Min((amount0 * totalSupply) / reserves.reserve0, (amount1 * totalSupply) / reserves.reserve1);
            }

            if (liquidity <= 0) throw new Exception("Insufficient liquidity minted");

            Mint(to, liquidity);

            Update(balance0, balance1, reserves);

            onMint(to, amount0, amount1);
        }

        public static void Burn(UInt160 to)
        {
            var reserves = GetReserves();
            var token0 = (UInt160)Token0.Get("token0");
            var token1 = (UInt160)Token1.Get("token1");
            var balance0 = (BigInteger)Contract.Call(token0, "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash);
            var balance1 = (BigInteger)Contract.Call(token1, "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash);
            var liquidity = (BigInteger)Balances.Get(Runtime.ExecutingScriptHash);

            var totalSupply = (BigInteger)TotalSupply.Get("total");
            var amount0 = liquidity * balance0 / totalSupply;
            var amount1 = liquidity * balance1 / totalSupply;

            if (amount0 <= 0 || amount1 <= 0) throw new Exception("Insufficient liquidity burned");

            Burn(Runtime.ExecutingScriptHash, liquidity);

            SafeTransfer(token0, to, amount0);
            SafeTransfer(token1, to, amount1);

            balance0 = (BigInteger)Contract.Call(token0, "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash);
            balance1 = (BigInteger)Contract.Call(token1, "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash);

            Update(balance0, balance1, reserves);

            onBurn(Runtime.CallingScriptHash, amount0, amount1, to);
        }

        public static void Swap(BigInteger amount0Out, BigInteger amount1Out, UInt160 to, byte[] data)
        {
            if (amount0Out <= 0 && amount1Out <= 0) throw new Exception("Insufficient output amount");
            var reserves = GetReserves();
            if (amount0Out > reserves.reserve0 || amount1Out > reserves.reserve1) throw new Exception("Insufficient liquidity");

            var token0 = (UInt160)Token0.Get("token0");
            var token1 = (UInt160)Token1.Get("token1");

            if (to == token0 || to == token1) throw new Exception("Invalid recipient");

            if (amount0Out > 0) SafeTransfer(token0, to, amount0Out);
            if (amount1Out > 0) SafeTransfer(token1, to, amount1Out);

            var balance0 = (BigInteger)Contract.Call(token0, "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash);
            var balance1 = (BigInteger)Contract.Call(token1, "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash);

            var amount0In = balance0 > reserves.reserve0 - amount0Out ? balance0 - (reserves.reserve0 - amount0Out) : 0;
            var amount1In = balance1 > reserves.reserve1 - amount1Out ? balance1 - (reserves.reserve1 - amount1Out) : 0;

            if (amount0In <= 0 && amount1In <= 0) throw new Exception("Insufficient input amount");

            var balance0Adjusted = balance0 * 1000 - amount0In * 3;
            var balance1Adjusted = balance1 * 1000 - amount1In * 3;

            if (balance0Adjusted * balance1Adjusted < reserves.reserve0 * reserves.reserve1 * 1000 * 1000) throw new Exception("Invalid K");

            Update(balance0, balance1, reserves);

            onSwap(Runtime.CallingScriptHash, amount0In, amount1In, amount0Out, amount1Out, to);
        }

        public static void Skim(UInt160 to)
        {
            var token0 = (UInt160)Token0.Get("token0");
            var token1 = (UInt160)Token1.Get("token1");
            var reserves = GetReserves();
            SafeTransfer(token0, to, (BigInteger)Contract.Call(token0, "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash) - reserves.reserve0);
            SafeTransfer(token1, to, (BigInteger)Contract.Call(token1, "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash) - reserves.reserve1);
        }

        public static void Sync()
        {
            var reserves = GetReserves();
            var balance0 = (BigInteger)Contract.Call((UInt160)Token0.Get("token0"), "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash);
            var balance1 = (BigInteger)Contract.Call((UInt160)Token1.Get("token1"), "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash);
            Update(balance0, balance1, reserves);
        }

        private static void Update(BigInteger balance0, BigInteger balance1, ReservesData reserves)
        {
            reserves.reserve0 = balance0;
            reserves.reserve1 = balance1;
            Reserves.Put("reserves", StdLib.Serialize(reserves));

            onSync(balance0, balance1);
        }

        private static void Mint(UInt160 to, BigInteger amount)
        {
            var totalSupply = (BigInteger)TotalSupply.Get("total");
            TotalSupply.Put("total", totalSupply + amount);

            var balance = (BigInteger)Balances.Get(to);
            Balances.Put(to, balance + amount);
        }

        private static void Burn(UInt160 from, BigInteger amount)
        {
            var totalSupply = (BigInteger)TotalSupply.Get("total");
            TotalSupply.Put("total", totalSupply - amount);

            var balance = (BigInteger)Balances.Get(from);
            Balances.Put(from, balance - amount);
        }

        private static void SafeTransfer(UInt160 token, UInt160 to, BigInteger amount)
        {
            if (amount > 0)
            {
                if ((bool)Contract.Call(token, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, to, amount, null) == false) throw new Exception("Transfer failed");
            }
        }

        [Safe]
        public static ReservesData GetReserves()
        {
            var data = Reserves.Get("reserves");
            if (data == null) return new ReservesData { reserve0 = 0, reserve1 = 0 };
            return (ReservesData)StdLib.Deserialize(data);
        }

        public struct ReservesData
        {
            public BigInteger reserve0;
            public BigInteger reserve1;
        }
    }
}
