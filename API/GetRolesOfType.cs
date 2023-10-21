namespace CustomProfiler.API;

using MonoMod.Utils;
using PlayerRoles;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using static HarmonyLib.AccessTools;

public static class GetRolesOfType
{
    static GetRolesOfType()
    {
        HashSet<Type> allowedTypes = AllowedTypes = [];

        Type[] allTypes = GetTypesFromAssembly(typeof(GameCore.Console).Assembly);

        for (int t = 0; t < allTypes.Length; t++)
        {
            if (!typeof(PlayerRoleBase).IsAssignableFrom(allTypes[t]))
                continue;

            if (allowedTypes.Add(allTypes[t]))
                Log.Info($"ROLE TYPE: {allTypes[t].FullName}");

            Type[] interfaces = allTypes[t].GetInterfaces();

            for (int i = 0; i < interfaces.Length; i++)
            {
                if (allowedTypes.Add(interfaces[i]))
                    Log.Info($"(INTERFACE) ROLE TYPE: {interfaces[i].FullName}");
            }
        }

        PlayerRoleManager.OnRoleChanged += CreateEvent();
    }

    private static PlayerRoleManager.RoleChanged CreateEvent()
    {
        DynamicMethod method = new("CustomProfiler.API.GetRolesOfType::OnRoleChanged", typeof(void), [typeof(ReferenceHub), typeof(PlayerRoleBase), typeof(PlayerRoleBase)]);

        ILGenerator generator = method.GetILGenerator();

        Dictionary<Type, LocalBuilder> typeLocals = [];
        Dictionary<Type, Type> roleHolders = [];

        foreach (Type type in AllowedTypes)
        {
            typeLocals.Add(type, generator.DeclareLocal(type));
            roleHolders.Add(type, typeof(RoleHolder<>).MakeGenericType(type));
        }

        foreach (Type type in AllowedTypes)
        {
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Isinst, type);
            generator.Emit(OpCodes.Stloc, typeLocals[type]);
        }

        Label nextLabel = generator.DefineLabel();
        bool first = true;

        foreach (Type type in AllowedTypes)
        {
            if (!first)
            {
                generator.MarkLabel(nextLabel);
                nextLabel = generator.DefineLabel();
            }

            generator.Emit(OpCodes.Ldloc, typeLocals[type]);
            generator.Emit(OpCodes.Brfalse_S, nextLabel);
            generator.Emit(OpCodes.Ldloc, typeLocals[type]);
            generator.Emit(OpCodes.Call, Method(roleHolders[type], "OnOldRole"));
            first = false;
        }

        first = true;
        foreach (Type type in AllowedTypes)
        {
            if (first)
            {
                generator.MarkLabel(nextLabel);
                nextLabel = generator.DefineLabel();
            }

            generator.Emit(OpCodes.Ldarg_2);
            generator.Emit(OpCodes.Isinst, type);
            generator.Emit(OpCodes.Stloc, typeLocals[type]);
        }

        nextLabel = generator.DefineLabel();

        foreach (Type type in AllowedTypes)
        {
            generator.MarkLabel(nextLabel);
            nextLabel = generator.DefineLabel();

            generator.Emit(OpCodes.Ldloc, typeLocals[type]);
            generator.Emit(OpCodes.Brfalse_S, nextLabel);
            generator.Emit(OpCodes.Ldloc, typeLocals[type]);
            generator.Emit(OpCodes.Call, Method(roleHolders[type], "OnNewRole"));
        }

        generator.MarkLabel(nextLabel);
        generator.Emit(OpCodes.Ret);

        return method.CreateDelegate<PlayerRoleManager.RoleChanged>();
    }

    public static List<T> Get<T>()
    {
        return RoleHolder<T>.Roles;
    }

    /// <summary>
    /// A collection of all allowed types to search as a role instance. This includes subclasses, as well as interfaces.
    /// </summary>
    public static readonly HashSet<Type> AllowedTypes;

    private static class RoleHolder<T>
    {
        static RoleHolder()
        {
            if (!AllowedTypes.Contains(typeof(T)))
                throw new InvalidOperationException($"The specified generic argument: '{typeof(T).FullName}' is not allowed!");

            _list = [];

            foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            {
                if (hub.roleManager.CurrentRole is T instance)
                    _list.Add(instance);
            }
        }

        private static readonly List<T> _list = [];

        public static List<T> Roles
        {
            get
            {
                return _list;
            }
        }

        private static void OnNewRole(T newRole)
        {
            Roles.Add(newRole);
            RemoveNull();
        }

        private static void OnOldRole(T oldRole)
        {
            Roles.Remove(oldRole);
            RemoveNull();
        }

        private static void RemoveNull() => _list.RemoveAll(IsNull);

        private static bool IsNull(T obj)
        {
            if (obj is not PlayerRoleBase role)
                return true;

            return role == null || role.Pooled;
        }
    }
}
