using EpicChain.SmartContract.Framework;
using System.Numerics;

namespace EpicChain.Contracts.Interfaces
{
    public interface IOracle
    {
        BigInteger GetPrice();
    }
}
