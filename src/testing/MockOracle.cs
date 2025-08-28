using EpicChain.SmartContract.Framework;
using System.ComponentModel;
using System.Numerics;

namespace EpicChain.Contracts.Testing
{
    [DisplayName("MockOracle")]
    public class MockOracle : SmartContract
    {
        private static readonly byte[] PriceKey = "price";

        [Safe]
        public static BigInteger GetPrice()
        {
            return (BigInteger)Storage.Get(Storage.CurrentContext, PriceKey);
        }

        public static void SetPrice(BigInteger price)
        {
            Storage.Put(Storage.CurrentContext, PriceKey, price);
        }
    }
}
