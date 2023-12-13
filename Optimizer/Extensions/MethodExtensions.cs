namespace Optimizer.Extensions;

using HarmonyLib;
using Mirror;
using PlayerRoles.FirstPersonControl;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

public static class MethodExtensions
{
    private static readonly Assembly AllowedAssembly = typeof(GameCore.Console).Assembly;

    private static readonly string[] DisallowedNamespaces = new string[]
    {
        "_Scripts.",
        "Achievements.",
        "AudioPooling.",
        "Authenticator.",
        "CameraShaking.",
        "CommandSystem.",
        "Cryptography.",
        "CursorManagement.",
        "CustomCulling.",
        "CustomRendering.",
        "DeathAnimations.",
        "Decals.",
        "GameCore.",
        "Hints.",
        "LiteNetLib.",
        "LiteNetLib4Mirror.",
        "MapGeneration.",
        "Microsoft.",
        "Mirror.",
        "RadialMenus.",
        "Security.",
        "Serialization.",
        "ServerOutput.",
        "Subtitles.",
        "System.",
        "Targeting.",
        "ToggleableMenus.",
        "UserSettings.",
        "Utf8Json.",
        "Utils.",
        "Waits.",
        "Windows.",
    };

    private static readonly Type[] DisallowedTypes = new Type[]
    {
        typeof(NetworkMessage),
        typeof(ServerConsole),
        typeof(StaticUnityMethods),
    };

    public static bool AllowsProfiling(this MethodInfo method)
    {
        // Must be the Assembly-CSharp assembly.
        if (method.DeclaringType.Assembly != AllowedAssembly)
            return false;

        // Disallowed namespaces
        if (method.DeclaringType.Namespace != null && DisallowedNamespaces.Any(method.DeclaringType.FullName.StartsWith))
            return false;

        // Disallowed types
        if (DisallowedTypes.Contains(method.DeclaringType))
            return false;
        if (DisallowedTypes.Any(x => x.IsAssignableFrom(method.DeclaringType)))
            return false;

        // Allow coroutine MoveNext functions
        if (method.IsCoroutineMoveNext())
            return true;

        // Don't allow generic methods
        if (method.IsGenericMethod)
            return false;

        // Don't allow constructors
        if (method.IsConstructor)
            return false;

        // Don't allow abstract members
        if (method.IsAbstract)
            return false;

        // Don't allow methods without a body
        if (!method.HasMethodBody())
            return false;

        // Don't allow RuntimeInitializeOnLoadMethod attributed methods to be patched
        // These methods are only run once
        if (method.IsRuntimeInitializeOnLoad())
            return false;

        bool compilerGenerated = method.IsCompilerGenerated();

        // Don't allow compiler generated property getters / setters
        if (compilerGenerated && method.IsGetterSetter())
            return false;

        // Don't allow compiler generated event adds / removes
        if (compilerGenerated && method.IsAddRemove())
            return false;

        // Don't allow operators
        if (method.IsOperator())
            return false;

        // Don't patch really small methods
        //if (method.GetMethodBody().GetILAsByteArray().Length < 30)
            //return false;

        // Don't allow methods that return IEnumerable
        if (method.ReturnsEnumerable())
            return false;

        return true;
    }

    public static bool IsRuntimeInitializeOnLoad(this MethodInfo method)
    {
        return method.IsDefined(typeof(RuntimeInitializeOnLoadMethodAttribute));
    }

    public static bool IsCompilerGenerated(this MethodInfo method)
    {
        return method.IsDefined(typeof(CompilerGeneratedAttribute), false);
    }

    public static bool IsGetterSetter(this MethodInfo method)
    {
        if (!method.IsSpecialName)
            return false;

        return method.Name.StartsWith("get_") || method.Name.StartsWith("set_");
    }

    public static bool IsAddRemove(this MethodInfo method)
    {
        if (!method.IsSpecialName)
            return false;

        return method.Name.StartsWith("add_") || method.Name.StartsWith("remove_");
    }

    public static bool IsOperator(this MethodInfo method)
    {
        if (!method.IsSpecialName)
            return false;

        return method.Name.StartsWith("op_");
    }

    public static bool IsCoroutineMoveNext(this MethodInfo method)
    {
        if (method.Name != "MoveNext")
            return false;

        if (method.ReturnType != typeof(bool))
            return false;

        if (method.GetParameters().Length != 0)
            return false;

        return typeof(IEnumerator<float>).IsAssignableFrom(method.DeclaringType);
    }

    public static bool ReturnsEnumerable(this MethodInfo method)
    {
        return typeof(IEnumerable).IsAssignableFrom(method.ReturnType);
    }
}
