namespace CustomProfiler.ProfilerUtils;

using CustomProfiler.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using static HarmonyLib.AccessTools;

/// <summary>
/// Creates dynamic methods which read the counts of collections within static fields across the appdomain.
/// </summary>
internal static class FieldCollectionReaders
{
    public delegate int GetCount();

    private static List<FieldInfo> allFields;

    public static Dictionary<FieldInfo, GetCount> getCount = new();

    private static Dictionary<Type, Action<FieldInfo, ILGenerator>> fieldProcessors = new()
    {
        { typeof(List<>), CountGetter.ImplementMethod },
        { typeof(HashSet<>), CountGetter.ImplementMethod },
        { typeof(Dictionary<,>), CountGetter.ImplementMethod },
        { typeof(ArrayList), CountGetter.ImplementMethod },
        { typeof(BitArray), CountGetter.ImplementMethod },
        { typeof(Stack<>), CountGetter.ImplementMethod },
        { typeof(Array), CountGetter.ImplementMethod },
        { typeof(Queue<>), CountGetter.ImplementMethod },
    };

    static FieldCollectionReaders()
    {
        Type[] types = typeof(GameCore.Console).Assembly.GetTypes();

        allFields = new();

        foreach (Type type in types)
        {
            foreach (FieldInfo field in type.GetFullyConstructedFields(includeNonPublic: true))
            {
                if (field.DeclaringType.GetCustomAttribute(typeof(CompilerGeneratedAttribute)) != null)
                    continue;

                Type fieldType = field.FieldType;

                if (!fieldType.IsGenericType)
                    continue;

                if (!fieldProcessors.ContainsKey(fieldType.GetGenericTypeDefinition()))
                    continue;

                allFields.Add(field);
            }
        }

        foreach (FieldInfo field in allFields)
        {
            ProcessField(field);
        }
    }

    public static IEnumerable<(FieldInfo, int)> GetCounts()
    {
        foreach (KeyValuePair<FieldInfo, GetCount> pair in getCount)
        {
            yield return (pair.Key, pair.Value());
        }
    }

    public static void ProcessField(FieldInfo field)
    {
        if (getCount.ContainsKey(field))
            return;

        Type genericTypeDef = field.FieldType;

        if (genericTypeDef.IsGenericType)
            genericTypeDef = genericTypeDef.GetGenericTypeDefinition();

        DynamicMethod method = new($"{field.DeclaringType.Name}.{field.Name}", typeof(int), [], true);

        fieldProcessors[genericTypeDef](field, method.GetILGenerator());

        getCount[field] = (GetCount)method.CreateDelegate(typeof(GetCount));
    }

    private static class CountGetter
    {
        public static void ImplementMethod(FieldInfo field, ILGenerator generator)
        {
            Label notNullLabel = generator.DefineLabel();

            // return field?.Count ?? 0;
            generator.Emit(OpCodes.Ldsfld, field);
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Brtrue_S, notNullLabel);
            generator.Emit(OpCodes.Pop);
            generator.Emit(OpCodes.Ldc_I4_0);
            generator.Emit(OpCodes.Ret);

            MethodInfo countGetter = PropertyGetter(field.FieldType, "Count");

            generator.MarkLabel(notNullLabel);
            generator.Emit(countGetter.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, countGetter);
            generator.Emit(OpCodes.Ret);
        }
    }
}
