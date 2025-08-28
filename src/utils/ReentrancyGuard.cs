using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;

namespace EpicChain.Contracts.Utils
{
    public abstract class ReentrancyGuard : SmartContract
    {
        private static StorageMap ReentrancyStatus => new StorageMap(Storage.CurrentContext, "reentrancy");

        protected static void Enter()
        {
            if ((bool)ReentrancyStatus.Get("entered")) throw new Exception("Re-entrant call");
            ReentrancyStatus.Put("entered", true);
        }

        protected static void Leave()
        {
            ReentrancyStatus.Put("entered", false);
        }
    }
}