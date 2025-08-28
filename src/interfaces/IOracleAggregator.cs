using EpicChain.SmartContract.Framework;
using System.Numerics;

namespace EpicChain.Contracts.Interfaces
{
    public interface IOracleAggregator
    {
        BigInteger GetPrice();
    }
}
