using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;
using EpicChain.Contracts.Interfaces;

namespace EpicChain.Contracts.Staking
{
    [DisplayName("Staking")]
    public class Staking : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] REWARD_DISTRIBUTOR_ROLE = "REWARD_DISTRIBUTOR_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        // Events
        public delegate void OnStaked(UInt160 user, BigInteger amount);
        public static event OnStaked onStaked;

        public delegate void OnUnstaked(UInt160 user, BigInteger amount);
        public static event OnUnstaked onUnstaked;

        public delegate void OnRewardsClaimed(UInt160 user, BigInteger amount);
        public static event OnRewardsClaimed onRewardsClaimed;

        // Storage
        private static StorageMap Stakes => new StorageMap(Storage.CurrentContext, "stakes");
        private static StorageMap Rewards => new StorageMap(Storage.CurrentContext, "rewards");
        private static StorageMap TotalStakedMap => new StorageMap(Storage.CurrentContext, "total_staked");

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
        /// Stakes governance tokens.
        /// </summary>
        /// <param name="user">The user staking tokens.</param>
        /// <param name="amount">The amount of tokens to stake.</param>
        public static void Stake(UInt160 user, BigInteger amount)
        {
            Pausable.RequireNotPaused();
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            if ((bool)Contract.Call(GovernanceTokenAddress, "transfer", CallFlags.All, user, Runtime.ExecutingScriptHash, amount, null) == false) throw new Exception("Governance token transfer failed");

            BigInteger currentStake = (BigInteger)Stakes.Get(user);
            Stakes.Put(user, currentStake + amount);

            BigInteger totalStaked = (BigInteger)TotalStakedMap.Get("total");
            TotalStakedMap.Put("total", totalStaked + amount);

            onStaked(user, amount);
        }

        /// <summary>
        /// Unstakes governance tokens.
        /// </summary>
        /// <param name="user">The user unstaking tokens.</param>
        /// <param name="amount">The amount of tokens to unstake.</param>
        public static void Unstake(UInt160 user, BigInteger amount)
        {
            Pausable.RequireNotPaused();
            if (!user.IsValid || amount <= 0) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            BigInteger currentStake = (BigInteger)Stakes.Get(user);
            if (currentStake < amount) throw new Exception("Insufficient stake");

            if ((bool)Contract.Call(GovernanceTokenAddress, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, user, amount, null) == false) throw new Exception("Governance token transfer failed");

            Stakes.Put(user, currentStake - amount);

            BigInteger totalStaked = (BigInteger)TotalStakedMap.Get("total");
            TotalStakedMap.Put("total", totalStaked - amount);

            onUnstaked(user, amount);
        }

        /// <summary>
        /// Distributes rewards to stakers.
        /// </summary>
        /// <param name="amount">The amount of rewards to distribute.</param>
        public static void DistributeRewards(BigInteger amount)
        {
            Roles.RequireRole(REWARD_DISTRIBUTOR_ROLE, (UInt160)Runtime.CallingScriptHash);

            BigInteger totalStaked = (BigInteger)TotalStakedMap.Get("total");
            if (totalStaked == 0) return;

            var stakers = Stakes.Find(FindOptions.KeysOnly | FindOptions.RemovePrefix);
            while (stakers.Next())
            {
                var staker = (UInt160)stakers.Value;
                BigInteger stakerBalance = (BigInteger)Stakes.Get(staker);
                BigInteger rewardShare = amount * stakerBalance / totalStaked;
                BigInteger currentReward = (BigInteger)Rewards.Get(staker);
                Rewards.Put(staker, currentReward + rewardShare);
            }
        }

        /// <summary>
        /// Claims the user's rewards.
        /// </summary>
        /// <param name="user">The user claiming rewards.</param>
        public static void ClaimRewards(UInt160 user)
        {
            if (!user.IsValid) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(user)) throw new Exception("No witness");

            BigInteger reward = (BigInteger)Rewards.Get(user);
            if (reward > 0)
            {
                Rewards.Delete(user);
                // Transfer rewards to the user (assuming GAS for now)
                if (GAS.Transfer(Runtime.ExecutingScriptHash, user, reward) == false) throw new Exception("Reward transfer failed");
            }

            onRewardsClaimed(user, reward);
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