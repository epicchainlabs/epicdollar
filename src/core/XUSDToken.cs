using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Attributes;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;

namespace EpicChain.Contracts.Core
{
    [DisplayName("EpicDollar")]
    [ManifestExtra("Author", "xmoohad")]
    [ManifestExtra("Email", "xmoohad@epic-chain.org")]
    [ManifestExtra("Description", "EpicDollar-XUSD-Contracts is an advanced suite of smart contracts meticulously crafted to manage the issuance, transfer, and stability of the EpicDollar (XUSD), a robust stablecoin within the EpicChain blockchain ecosystem")]
    [SupportedStandards("XEP-17")]
    public class XUSDToken : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] MINTER_ROLE = "MINTER_ROLE";
        public static readonly byte[] BURNER_ROLE = "BURNER_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        const string SYMBOL = "XUSD";
        const byte DECIMALS = 8;
        const long INITIAL_SUPPLY = 0; // No initial supply, minted by vault

        public delegate void OnTransferDelegate(UInt160 from, UInt160 to, BigInteger amount);

        [DisplayName("Transfer")]
        public static event OnTransferDelegate OnTransfer = default!;

        const byte Prefix_TotalSupply = 0x00;
        const byte Prefix_Balance = 0x01;

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

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;
            var tx = (Transaction)Runtime.ScriptContainer;
            Roles.GrantRole(DEFAULT_ADMIN_ROLE, tx.Sender);
            Roles.GrantRole(MINTER_ROLE, tx.Sender);
            Roles.GrantRole(BURNER_ROLE, tx.Sender);
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
    }
}