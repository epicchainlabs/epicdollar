using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;

namespace EpicChain.Contracts.Utils
{
    public static class Roles
    {
        public struct Role
        {
            public UInt160[] members;
        }

        private static StorageMap RolesMap => new StorageMap(Storage.CurrentContext, "roles");

        public static Role GetRole(byte[] roleName)
        {
            var data = RolesMap[roleName];
            if (data == null) return new Role { members = new UInt160[0] };
            return (Role)StdLib.Deserialize(data);
        }

        private static void SaveRole(byte[] roleName, Role role)
        {
            RolesMap.Put(roleName, StdLib.Serialize(role));
        }

        public static void GrantRole(byte[] roleName, UInt160 member)
        {
            var role = GetRole(roleName);
            var members = role.members;
            if (HasRole(roleName, member)) return;
            var newMembers = new UInt160[members.Length + 1];
            for (int i = 0; i < members.Length; i++)
            {
                newMembers[i] = members[i];
            }
            newMembers[members.Length] = member;
            role.members = newMembers;
            SaveRole(roleName, role);
        }

        public static void RevokeRole(byte[] roleName, UInt160 member)
        {
            var role = GetRole(roleName);
            var members = role.members;
            if (!HasRole(roleName, member)) return;
            var newMembers = new UInt160[members.Length - 1];
            int j = 0;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] != member)
                {
                    newMembers[j] = members[i];
                    j++;
                }
            }
            role.members = newMembers;
            SaveRole(roleName, role);
        }

        public static bool HasRole(byte[] roleName, UInt160 member)
        {
            var role = GetRole(roleName);
            var members = role.members;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == member)
                {
                    return true;
                }
            }
            return false;
        }

        public static void RequireRole(byte[] roleName, UInt160 member)
        {
            if (!HasRole(roleName, member)) throw new Exception("Missing role");
        }
    }
}