using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;

namespace EpicChain.Contracts.Utils
{
    public abstract class Pausable : SmartContract
    {
        public static readonly byte[] PAUSER_ROLE = "PAUSER_ROLE";

        private static StorageMap PausedMap => new StorageMap(Storage.CurrentContext, "paused");

        [Safe]
        public static bool IsPaused()
        {
            return (bool)PausedMap.Get("paused");
        }

        protected static void Pause()
        {
            RequireNotPaused();
            Roles.RequireRole(PAUSER_ROLE, (UInt160)Runtime.CallingScriptHash);
            PausedMap.Put("paused", true);
        }

        protected static void Unpause()
        {
            RequirePaused();
            Roles.RequireRole(PAUSER_ROLE, (UInt160)Runtime.CallingScriptHash);
            PausedMap.Put("paused", false);
        }

        protected static void RequireNotPaused()
        {
            if (IsPaused()) throw new Exception("Contract is paused");
        }

        protected static void RequirePaused()
        {
            if (!IsPaused()) throw new Exception("Contract is not paused");
        }
    }
}