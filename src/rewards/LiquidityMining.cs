using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;

namespace EpicChain.Contracts.Rewards
{
    [DisplayName("LiquidityMining")]
    public class LiquidityMining : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        // Events
        public delegate void OnDeposit(UInt160 user, BigInteger pid, BigInteger amount);
        public static event OnDeposit onDeposit;

        public delegate void OnWithdraw(UInt160 user, BigInteger pid, BigInteger amount);
        public static event OnWithdraw onWithdraw;

        public delegate void OnClaim(UInt160 user, BigInteger pid, BigInteger amount);
        public static event OnClaim onClaim;

        // Storage
        private static StorageMap PoolInfo => new StorageMap(Storage.CurrentContext, "pool_info");
        private static StorageMap UserInfo => new StorageMap(Storage.CurrentContext, "user_info");
        private static StorageMap TotalAllocPoint => new StorageMap(Storage.CurrentContext, "total_alloc_point");

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
        /// Adds a new liquidity pool to the contract.
        /// </summary>
        /// <param name="allocPoint">The allocation points for the new pool.</param>
        /// <param name="lpToken">The address of the LP token for the new pool.</param>
        /// <param name="withUpdate">Whether to update the rewards for all pools.</param>
        public static void Add(BigInteger allocPoint, UInt160 lpToken, bool withUpdate)
        {
            Roles.RequireRole(ADMIN_ROLE, (UInt160)Runtime.CallingScriptHash);
            if (withUpdate) MassUpdatePools();

            BigInteger totalAllocPoint = (BigInteger)TotalAllocPoint.Get("total");
            TotalAllocPoint.Put("total", totalAllocPoint + allocPoint);

            var pool = new Pool
            {
                lpToken = lpToken,
                allocPoint = allocPoint,
                lastRewardBlock = Runtime.Height,
                accRewardsPerShare = 0
            };

            var poolId = PoolInfo.Get("length");
            PoolInfo.Put(poolId, StdLib.Serialize(pool));
            PoolInfo.Put("length", (BigInteger)poolId + 1);
        }

        /// <summary>
        /// Updates the allocation points for a liquidity pool.
        /// </summary>
        /// <param name="pid">The ID of the pool to update.</param>
        /// <param name="allocPoint">The new allocation points for the pool.</param>
        /// <param name="withUpdate">Whether to update the rewards for all pools.</param>
        public static void Set(BigInteger pid, BigInteger allocPoint, bool withUpdate)
        {
            Roles.RequireRole(ADMIN_ROLE, (UInt160)Runtime.CallingScriptHash);
            if (withUpdate) MassUpdatePools();

            var pool = GetPool(pid);
            BigInteger totalAllocPoint = (BigInteger)TotalAllocPoint.Get("total");
            TotalAllocPoint.Put("total", totalAllocPoint - pool.allocPoint + allocPoint);

            pool.allocPoint = allocPoint;
            PoolInfo.Put(pid.ToByteArray(), StdLib.Serialize(pool));
        }

        /// <summary>
        /// Deposits LP tokens to start earning rewards.
        /// </summary>
        /// <param name="pid">The ID of the pool to deposit to.</param>
        /// <param name="amount">The amount of LP tokens to deposit.</param>
        public static void Deposit(BigInteger pid, BigInteger amount)
        {
            var pool = GetPool(pid);
            var user = (UInt160)Runtime.CallingScriptHash;
            var userInfo = GetUserInfo(pid, user);

            UpdatePool(pid);

            if (userInfo.amount > 0)
            {
                BigInteger pending = userInfo.amount * pool.accRewardsPerShare / 1_000_000_000_000 - userInfo.rewardDebt;
                if (pending > 0)
                {
                    SafeRewardTransfer(user, pending);
                }
            }

            if (amount > 0)
            {
                if ((bool)Contract.Call(pool.lpToken, "transfer", CallFlags.All, user, Runtime.ExecutingScriptHash, amount, null) == false) throw new Exception("LP token transfer failed");
                userInfo.amount += amount;
            }

            userInfo.rewardDebt = userInfo.amount * pool.accRewardsPerShare / 1_000_000_000_000;
            UserInfo.Put(pid.ToByteArray().Concat(user), StdLib.Serialize(userInfo));

            onDeposit(user, pid, amount);
        }

        /// <summary>
        /// Withdraws LP tokens.
        /// </summary>
        /// <param name="pid">The ID of the pool to withdraw from.</param>
        /// <param name="amount">The amount of LP tokens to withdraw.</param>
        public static void Withdraw(BigInteger pid, BigInteger amount)
        {
            var pool = GetPool(pid);
            var user = (UInt160)Runtime.CallingScriptHash;
            var userInfo = GetUserInfo(pid, user);

            if (userInfo.amount < amount) throw new Exception("Insufficient balance");

            UpdatePool(pid);

            BigInteger pending = userInfo.amount * pool.accRewardsPerShare / 1_000_000_000_000 - userInfo.rewardDebt;
            if (pending > 0)
            {
                SafeRewardTransfer(user, pending);
            }

            if (amount > 0)
            {
                if ((bool)Contract.Call(pool.lpToken, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, user, amount, null) == false) throw new Exception("LP token transfer failed");
                userInfo.amount -= amount;
            }

            userInfo.rewardDebt = userInfo.amount * pool.accRewardsPerShare / 1_000_000_000_000;
            UserInfo.Put(pid.ToByteArray().Concat(user), StdLib.Serialize(userInfo));

            onWithdraw(user, pid, amount);
        }

        /// <summary>
        /// Claims rewards for a specific pool.
        /// </summary>
        /// <param name="pid">The ID of the pool to claim rewards from.</param>
        public static void Claim(BigInteger pid)
        {
            var pool = GetPool(pid);
            var user = (UInt160)Runtime.CallingScriptHash;
            var userInfo = GetUserInfo(pid, user);

            UpdatePool(pid);

            BigInteger pending = userInfo.amount * pool.accRewardsPerShare / 1_000_000_000_000 - userInfo.rewardDebt;
            if (pending > 0)
            {
                SafeRewardTransfer(user, pending);
            }

            userInfo.rewardDebt = userInfo.amount * pool.accRewardsPerShare / 1_000_000_000_000;
            UserInfo.Put(pid.ToByteArray().Concat(user), StdLib.Serialize(userInfo));

            onClaim(user, pid, pending);
        }

        private static void MassUpdatePools()
        {
            var length = (BigInteger)PoolInfo.Get("length");
            for (BigInteger i = 0; i < length; i++)
            {
                UpdatePool(i);
            }
        }

        private static void UpdatePool(BigInteger pid)
        {
            var pool = GetPool(pid);
            if (Runtime.Height <= pool.lastRewardBlock) return;

            var lpSupply = (BigInteger)Contract.Call(pool.lpToken, "balanceOf", CallFlags.ReadOnly, Runtime.ExecutingScriptHash);
            if (lpSupply == 0) return;

            var totalAllocPoint = (BigInteger)TotalAllocPoint.Get("total");
            var rewardsPerBlock = GetRewardsPerBlock();
            var multiplier = GetMultiplier(pool.lastRewardBlock, Runtime.Height);
            var reward = multiplier * rewardsPerBlock * pool.allocPoint / totalAllocPoint;

            pool.accRewardsPerShare += reward * 1_000_000_000_000 / lpSupply;
            pool.lastRewardBlock = Runtime.Height;
            PoolInfo.Put(pid.ToByteArray(), StdLib.Serialize(pool));
        }

        private static BigInteger GetRewardsPerBlock()
        {
            // This can be a fixed value or a more complex calculation
            return 100000000; // 1 EPG per block
        }

        private static BigInteger GetMultiplier(uint from, uint to)
        {
            return to - from;
        }

        private static void SafeRewardTransfer(UInt160 to, BigInteger amount)
        {
            if (amount > 0)
            {
                if ((bool)Contract.Call(GovernanceTokenAddress, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, to, amount, null) == false) throw new Exception("Reward transfer failed");
            }
        }

        [Safe]
        public static Pool GetPool(BigInteger pid)
        {
            var data = PoolInfo.Get(pid.ToByteArray());
            if (data == null) throw new Exception("Pool not found");
            return (Pool)StdLib.Deserialize(data);
        }

        [Safe]
        public static UserInfo GetUserInfo(BigInteger pid, UInt160 user)
        {
            var data = UserInfo.Get(pid.ToByteArray().Concat(user));
            if (data == null) return new UserInfo { amount = 0, rewardDebt = 0 };
            return (UserInfo)StdLib.Deserialize(data);
        }

        public struct Pool
        {
            public UInt160 lpToken;
            public BigInteger allocPoint;
            public uint lastRewardBlock;
            public BigInteger accRewardsPerShare;
        }

        public struct UserInfo
        {
            public BigInteger amount;
            public BigInteger rewardDebt;
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