using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Attributes;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;

namespace EpicChain.Contracts.Governance
{
    [DisplayName("EpicGovernanceToken")]
    [SupportedStandards("XEP-17")]
    public class GovernanceToken : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] MINTER_ROLE = "MINTER_ROLE";
        public static readonly byte[] BURNER_ROLE = "BURNER_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        const string SYMBOL = "EPG";
        const byte DECIMALS = 8;

        public delegate void OnTransferDelegate(UInt160 from, UInt160 to, BigInteger amount);
        [DisplayName("Transfer")]
        public static event OnTransferDelegate OnTransfer = default!;

        public delegate void OnDelegateChanged(UInt160 delegator, UInt160 fromDelegate, UInt160 toDelegate);
        [DisplayName("DelegateChanged")]
        public static event OnDelegateChanged onDelegateChanged;

        public delegate void OnDelegateVotesChanged(UInt160 delegateAddress, BigInteger previousBalance, BigInteger newBalance);
        [DisplayName("DelegateVotesChanged")]
        public static event OnDelegateVotesChanged onDelegateVotesChanged;

        const byte Prefix_TotalSupply = 0x00;
        const byte Prefix_Balance = 0x01;
        const byte Prefix_Checkpoints = 0x02;
        const byte Prefix_Delegates = 0x03;
        const byte Prefix_NumCheckpoints = 0x04;

        [Safe]
        public static string Symbol() => SYMBOL;

        [Safe]
        public static byte Decimals() => DECIMALS;

        [Safe]
        public static BigInteger TotalSupply() => (BigInteger)Storage.Get(Storage.CurrentContext, new byte[] { Prefix_TotalSupply });

        [Safe]
        public static BigInteger BalanceOf(UInt160 owner)
        {
            if (owner is null || !owner.IsValid) throw new Exception("The argument \"owner\" is invalid.");
            StorageMap balanceMap = new(Storage.CurrentContext, Prefix_Balance);
            return (BigInteger)balanceMap[owner];
        }

        public static bool Transfer(UInt160 from, UInt160 to, BigInteger amount, object data)
        {
            if (from is null || !from.IsValid) throw new Exception("The argument \"from\" is invalid.");
            if (to is null || !to.IsValid) throw new Exception("The argument \"to\" is invalid.");
            if (amount < 0) throw new Exception("The amount must be a positive number.");
            if (!Runtime.CheckWitness(from)) return false;
            if (amount != 0)
            {
                if (!UpdateBalance(from, -amount)) return false;
                UpdateBalance(to, +amount);
            }
            PostTransfer(from, to, amount, data);
            return true;
        }

        public static void Mint(UInt160 account, BigInteger amount)
        {
            Roles.RequireRole(MINTER_ROLE, (UInt160)Runtime.CallingScriptHash);
            if (amount.IsZero) return;
            if (amount.Sign < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            CreateTokens(account, amount);
        }

        public static void Burn(UInt160 account, BigInteger amount)
        {
            Roles.RequireRole(BURNER_ROLE, (UInt160)Runtime.CallingScriptHash);
            if (amount.IsZero) return;
            if (amount.Sign < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!UpdateBalance(account, -amount)) throw new InvalidOperationException();
            UpdateTotalSupply(-amount);
            PostTransfer(account, null, amount, null);
        }

        public static void Delegate(UInt160 delegator, UInt160 delegatee)
        {
            if (!delegator.IsValid || !delegatee.IsValid) throw new Exception("Invalid arguments");
            if (!Runtime.CheckWitness(delegator)) throw new Exception("No witness");

            StorageMap delegates = new(Storage.CurrentContext, Prefix_Delegates);
            var fromDelegate = (UInt160)delegates.Get(delegator);
            delegates.Put(delegator, delegatee);

            onDelegateChanged(delegator, fromDelegate, delegatee);

            MoveDelegates(fromDelegate, delegatee, BalanceOf(delegator));
        }

        [Safe]
        public static BigInteger GetVotes(UInt160 account)
        {
            StorageMap checkpoints = new(Storage.CurrentContext, Prefix_Checkpoints);
            var numCheckpoints = (BigInteger)checkpoints.Get(account.Concat(Prefix_NumCheckpoints));
            if (numCheckpoints == 0) return 0;

            var checkpoint = (Checkpoint)StdLib.Deserialize(checkpoints.Get(account.Concat(numCheckpoints.ToByteArray())));
            return checkpoint.votes;
        }

        [Safe]
        public static BigInteger GetPastVotes(UInt160 account, uint blockNumber)
        {
            if (blockNumber >= Runtime.Height) throw new Exception("Block number must be in the past");

            StorageMap checkpoints = new(Storage.CurrentContext, Prefix_Checkpoints);
            var numCheckpoints = (BigInteger)checkpoints.Get(account.Concat(Prefix_NumCheckpoints));
            if (numCheckpoints == 0) return 0;

            // Binary search for the checkpoint
            BigInteger lower = 0;
            BigInteger upper = numCheckpoints - 1;
            BigInteger mid = 0;
            while (lower < upper)
            {
                mid = (lower + upper + 1) / 2;
                var checkpoint = (Checkpoint)StdLib.Deserialize(checkpoints.Get(account.Concat(mid.ToByteArray())));
                if (checkpoint.fromBlock == blockNumber)
                {
                    return checkpoint.votes;
                }
                else if (checkpoint.fromBlock < blockNumber)
                {
                    lower = mid;
                }
                else
                {
                    upper = mid - 1;
                }
            }

            var finalCheckpoint = (Checkpoint)StdLib.Deserialize(checkpoints.Get(account.Concat(lower.ToByteArray())));
            return finalCheckpoint.votes;
        }

        private static void MoveDelegates(UInt160 from, UInt160 to, BigInteger amount)
        {
            if (from != to && amount > 0)
            {
                if (from != null)
                {
                    var fromCheckpoints = new StorageMap(Storage.CurrentContext, Prefix_Checkpoints);
                    var fromNumCheckpoints = (BigInteger)fromCheckpoints.Get(from.Concat(Prefix_NumCheckpoints));
                    var fromOldVotes = fromNumCheckpoints > 0 ? ((Checkpoint)StdLib.Deserialize(fromCheckpoints.Get(from.Concat((fromNumCheckpoints - 1).ToByteArray()))).votes : 0;
                    var fromNewVotes = fromOldVotes - amount;
                    WriteCheckpoint(from, fromNumCheckpoints, fromOldVotes, fromNewVotes);
                }

                if (to != null)
                {
                    var toCheckpoints = new StorageMap(Storage.CurrentContext, Prefix_Checkpoints);
                    var toNumCheckpoints = (BigInteger)toCheckpoints.Get(to.Concat(Prefix_NumCheckpoints));
                    var toOldVotes = toNumCheckpoints > 0 ? ((Checkpoint)StdLib.Deserialize(toCheckpoints.Get(to.Concat((toNumCheckpoints - 1).ToByteArray()))).votes : 0;
                    var toNewVotes = toOldVotes + amount;
                    WriteCheckpoint(to, toNumCheckpoints, toOldVotes, toNewVotes);
                }
            }
        }

        private static void WriteCheckpoint(UInt160 owner, BigInteger numCheckpoints, BigInteger oldVotes, BigInteger newVotes)
        {
            var blockNumber = Runtime.Height;

            if (numCheckpoints > 0)
            {
                var lastCheckpoint = (Checkpoint)StdLib.Deserialize(new StorageMap(Storage.CurrentContext, Prefix_Checkpoints).Get(owner.Concat((numCheckpoints - 1).ToByteArray())));
                if (lastCheckpoint.fromBlock == blockNumber)
                {
                    lastCheckpoint.votes = newVotes;
                    new StorageMap(Storage.CurrentContext, Prefix_Checkpoints).Put(owner.Concat((numCheckpoints - 1).ToByteArray()), StdLib.Serialize(lastCheckpoint));
                    return;
                }
            }

            new StorageMap(Storage.CurrentContext, Prefix_Checkpoints).Put(owner.Concat(numCheckpoints.ToByteArray()), StdLib.Serialize(new Checkpoint { fromBlock = blockNumber, votes = newVotes }));
            new StorageMap(Storage.CurrentContext, Prefix_Checkpoints).Put(owner.Concat(Prefix_NumCheckpoints), numCheckpoints + 1);

            onDelegateVotesChanged(owner, oldVotes, newVotes);
        }

        static void PostTransfer(UInt160? from, UInt160? to, BigInteger amount, object? data)
        {
            from ??= UInt160.Zero;
            to ??= UInt160.Zero;
            OnTransfer(from, to, amount);
            if (to is not null && ContractManagement.GetContract(to) is not null)
                Contract.Call(to, "onNEP17Payment", CallFlags.All, from, amount, data);
        }

        static void UpdateTotalSupply(BigInteger increment)
        {
            StorageContext context = Storage.CurrentContext;
            byte[] key = new byte[] { Prefix_TotalSupply };
            BigInteger totalSupply = (BigInteger)Storage.Get(context, key);
            totalSupply += increment;
            Storage.Put(context, key, totalSupply);
        }

        static bool UpdateBalance(UInt160 owner, BigInteger increment)
        {
            StorageMap balanceMap = new(Storage.CurrentContext, Prefix_Balance);
            BigInteger balance = (BigInteger)balanceMap[owner];
            balance += increment;
            if (balance < 0) return false;
            if (balance.IsZero)
                balanceMap.Delete(owner);
            else
                balanceMap.Put(owner, balance);
            return true;
        }

        static void CreateTokens(UInt160 account, BigInteger amount)
        {
            UpdateBalance(account, +amount);
            UpdateTotalSupply(+amount);
            PostTransfer(null, account, amount, null);
        }

        public struct Checkpoint
        {
            public uint fromBlock;
            public BigInteger votes;
        }
    }
}