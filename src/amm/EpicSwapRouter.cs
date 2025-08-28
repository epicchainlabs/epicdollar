using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;

namespace EpicChain.Contracts.AMM
{
    [DisplayName("EpicSwapRouter")]
    public class EpicSwapRouter : SmartContract
    {
        // Storage
        private static StorageMap Factory => new StorageMap(Storage.CurrentContext, "factory");

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;
            Factory.Put("factory", (UInt160)data);
        }

        public static void AddLiquidity(UInt160 tokenA, UInt160 tokenB, BigInteger amountADesired, BigInteger amountBDesired, BigInteger amountAMin, BigInteger amountBMin, UInt160 to, BigInteger deadline)
        {
            if (Runtime.Time > deadline) throw new Exception("Deadline expired");

            var (amountA, amountB) = _AddLiquidity(tokenA, tokenB, amountADesired, amountBDesired, amountAMin, amountBMin);

            var pair = EpicSwapFactory.GetPair(tokenA, tokenB);
            SafeTransferFrom(tokenA, (UInt160)Runtime.CallingScriptHash, pair, amountA);
            SafeTransferFrom(tokenB, (UInt160)Runtime.CallingScriptHash, pair, amountB);

            Contract.Call(pair, "mint", CallFlags.All, to);
        }

        public static void RemoveLiquidity(UInt160 tokenA, UInt160 tokenB, BigInteger liquidity, BigInteger amountAMin, BigInteger amountBMin, UInt160 to, BigInteger deadline)
        {
            if (Runtime.Time > deadline) throw new Exception("Deadline expired");

            var pair = EpicSwapFactory.GetPair(tokenA, tokenB);
            SafeTransferFrom(pair, (UInt160)Runtime.CallingScriptHash, pair, liquidity);

            var amounts = (BigInteger[])Contract.Call(pair, "burn", CallFlags.All, to);
            if (amounts[0] < amountAMin || amounts[1] < amountBMin) throw new Exception("Insufficient amount");
        }

        public static void SwapExactTokensForTokens(BigInteger amountIn, BigInteger amountOutMin, UInt160[] path, UInt160 to, BigInteger deadline)
        {
            if (Runtime.Time > deadline) throw new Exception("Deadline expired");

            var amounts = GetAmountsOut(amountIn, path);
            if (amounts[amounts.Length - 1] < amountOutMin) throw new Exception("Insufficient output amount");

            SafeTransferFrom(path[0], (UInt160)Runtime.CallingScriptHash, EpicSwapFactory.GetPair(path[0], path[1]), amounts[0]);
            _Swap(amounts, path, to);
        }

        public static void SwapTokensForExactTokens(BigInteger amountOut, BigInteger amountInMax, UInt160[] path, UInt160 to, BigInteger deadline)
        {
            if (Runtime.Time > deadline) throw new Exception("Deadline expired");

            var amounts = GetAmountsIn(amountOut, path);
            if (amounts[0] > amountInMax) throw new Exception("Excessive input amount");

            SafeTransferFrom(path[0], (UInt160)Runtime.CallingScriptHash, EpicSwapFactory.GetPair(path[0], path[1]), amounts[0]);
            _Swap(amounts, path, to);
        }

        private static (BigInteger, BigInteger) _AddLiquidity(UInt160 tokenA, UInt160 tokenB, BigInteger amountADesired, BigInteger amountBDesired, BigInteger amountAMin, BigInteger amountBMin)
        {
            if (EpicSwapFactory.GetPair(tokenA, tokenB) == null)
            {
                EpicSwapFactory.CreatePair(tokenA, tokenB);
            }

            var reserves = EpicSwapPair.GetReserves();
            if (reserves.reserve0 == 0 && reserves.reserve1 == 0)
            {
                return (amountADesired, amountBDesired);
            }

            var amountBOptimal = Quote(amountADesired, reserves.reserve0, reserves.reserve1);
            if (amountBOptimal <= amountBDesired)
            {
                if (amountBOptimal < amountBMin) throw new Exception("Insufficient B amount");
                return (amountADesired, amountBOptimal);
            }
            else
            {
                var amountAOptimal = Quote(amountBDesired, reserves.reserve1, reserves.reserve0);
                if (amountAOptimal < amountAMin) throw new Exception("Insufficient A amount");
                return (amountAOptimal, amountBDesired);
            }
        }

        private static void _Swap(BigInteger[] amounts, UInt160[] path, UInt160 _to)
        {
            for (int i = 0; i < path.Length - 1; i++)
            {
                var input = path[i];
                var output = path[i + 1];
                var pair = EpicSwapFactory.GetPair(input, output);

                var amountOut = amounts[i + 1];
                var (amount0Out, amount1Out) = input.ToBigInteger() < output.ToBigInteger() ? (BigInteger.Zero, amountOut) : (amountOut, BigInteger.Zero);

                var to = i < path.Length - 2 ? EpicSwapFactory.GetPair(output, path[i + 2]) : _to;
                Contract.Call(pair, "swap", CallFlags.All, amount0Out, amount1Out, to, null);
            }
        }

        [Safe]
        public static BigInteger Quote(BigInteger amountA, BigInteger reserveA, BigInteger reserveB)
        {
            if (amountA <= 0) throw new Exception("Insufficient amount");
            if (reserveA <= 0 || reserveB <= 0) throw new Exception("Insufficient liquidity");
            return amountA * reserveB / reserveA;
        }

        [Safe]
        public static BigInteger GetAmountOut(BigInteger amountIn, BigInteger reserveIn, BigInteger reserveOut)
        {
            if (amountIn <= 0) throw new Exception("Insufficient amount");
            if (reserveIn <= 0 || reserveOut <= 0) throw new Exception("Insufficient liquidity");

            var amountInWithFee = amountIn * 997;
            var numerator = amountInWithFee * reserveOut;
            var denominator = reserveIn * 1000 + amountInWithFee;
            return numerator / denominator;
        }

        [Safe]
        public static BigInteger GetAmountIn(BigInteger amountOut, BigInteger reserveIn, BigInteger reserveOut)
        {
            if (amountOut <= 0) throw new Exception("Insufficient amount");
            if (reserveIn <= 0 || reserveOut <= 0) throw new Exception("Insufficient liquidity");

            var numerator = reserveIn * amountOut * 1000;
            var denominator = (reserveOut - amountOut) * 997;
            return (numerator / denominator) + 1;
        }

        [Safe]
        public static BigInteger[] GetAmountsOut(BigInteger amountIn, UInt160[] path)
        {
            if (path.Length < 2) throw new Exception("Invalid path");
            var amounts = new BigInteger[path.Length];
            amounts[0] = amountIn;
            for (int i = 0; i < path.Length - 1; i++)
            {
                var reserves = EpicSwapPair.GetReserves();
                amounts[i + 1] = GetAmountOut(amounts[i], reserves.reserve0, reserves.reserve1);
            }
            return amounts;
        }

        [Safe]
        public static BigInteger[] GetAmountsIn(BigInteger amountOut, UInt160[] path)
        {
            if (path.Length < 2) throw new Exception("Invalid path");
            var amounts = new BigInteger[path.Length];
            amounts[amounts.Length - 1] = amountOut;
            for (int i = path.Length - 1; i > 0; i--)
            {
                var reserves = EpicSwapPair.GetReserves();
                amounts[i - 1] = GetAmountIn(amounts[i], reserves.reserve0, reserves.reserve1);
            }
            return amounts;
        }

        private static void SafeTransferFrom(UInt160 token, UInt160 from, UInt160 to, BigInteger amount)
        {
            if (amount > 0)
            {
                if ((bool)Contract.Call(token, "transferFrom", CallFlags.All, from, to, amount, null) == false) throw new Exception("TransferFrom failed");
            }
        }
    }
}
