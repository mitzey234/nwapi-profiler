﻿namespace CustomProfiler.Extensions;

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

public static class TypeExtensions
{
    /// <remarks>This gets methods within contructed generic types. Note that generic methods are skipped.</remarks>
    public static IEnumerable<MethodInfo> GetFullyConstructedMethods(this Type type, bool includeNonPublic)
    {
        if (type.IsGenericType && !type.IsConstructedGenericType)
        {
            yield break;
        }

        BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;

        if (includeNonPublic)
        {
            flags |= BindingFlags.NonPublic;
        }

        while (type != null)
        {
            MethodInfo[] methods = type.GetMethods(flags);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];

                if (m.IsGenericMethod)
                    continue;

                if (!m.HasMethodBody())
                    continue;

                yield return m;
            }

            type = type.BaseType;
        }
    }

    /// <remarks>This only gets static fields within contructed generic types.</remarks>
    public static IEnumerable<FieldInfo> GetFullyConstructedFields(this Type type, bool includeNonPublic)
    {
        if (type.IsGenericType && !type.IsConstructedGenericType)
        {
            yield break;
        }

        BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;

        if (includeNonPublic)
        {
            flags |= BindingFlags.NonPublic;
        }

        while (type != null)
        {
            FieldInfo[] fields = type.GetFields(flags);

            for (int i = 0; i < fields.Length; i++)
            {
                yield return fields[i];
            }

            type = type.BaseType;
        }
    }
}
