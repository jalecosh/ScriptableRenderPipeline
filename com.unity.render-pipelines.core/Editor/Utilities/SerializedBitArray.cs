using UnityEngine.Rendering;
using System;
using System.Reflection;
using System.Linq.Expressions;

namespace UnityEditor.Rendering
{
    public static class SerializedBitArrayUtilities
    {
        // Note: this should be exposed at the same time as issue with type other than Int32 is fixed on C++ side
        static Action<SerializedProperty, int, bool> SetBitAtIndexForAllTargetsImmediate;
        static Func<SerializedProperty, int> HasMultipleDifferentValuesBitwise;
        static SerializedBitArrayUtilities()
        {
            var type = typeof(SerializedProperty);
            var setBitAtIndexForAllTargetsImmediateMethodInfo = type.GetMethod("SetBitAtIndexForAllTargetsImmediate", BindingFlags.Instance | BindingFlags.NonPublic);
            var hasMultipleDifferentValuesBitwisePropertyInfo = type.GetProperty("hasMultipleDifferentValuesBitwise", BindingFlags.Instance | BindingFlags.NonPublic);
            var serializedPropertyParameter = Expression.Parameter(typeof(SerializedProperty), "property");
            var indexParameter = Expression.Parameter(typeof(int), "index");
            var valueParameter = Expression.Parameter(typeof(bool), "value");
            var hasMultipleDifferentValuesBitwiseProperty = Expression.Property(serializedPropertyParameter, hasMultipleDifferentValuesBitwisePropertyInfo);
            var setBitAtIndexForAllTargetsImmediateCall = Expression.Call(serializedPropertyParameter, setBitAtIndexForAllTargetsImmediateMethodInfo, indexParameter, valueParameter);
            var setBitAtIndexForAllTargetsImmediateLambda = Expression.Lambda<Action<SerializedProperty, int, bool>>(setBitAtIndexForAllTargetsImmediateCall, serializedPropertyParameter, indexParameter, valueParameter);
            var hasMultipleDifferentValuesBitwiseLambda = Expression.Lambda<Func<SerializedProperty, int>>(hasMultipleDifferentValuesBitwiseProperty, serializedPropertyParameter);
            SetBitAtIndexForAllTargetsImmediate = setBitAtIndexForAllTargetsImmediateLambda.Compile();
            HasMultipleDifferentValuesBitwise = hasMultipleDifferentValuesBitwiseLambda.Compile();
        }

        public static uint GetBitArrayCapacity(this SerializedProperty property)
        {
            const string baseTypeName = "BitArray";
            string type = property.type;
            uint capacity;
            if (type.StartsWith(baseTypeName) && uint.TryParse(type.Substring(baseTypeName.Length), out capacity))
                return capacity;
            throw new ArgumentException("Trying to call Get on unknown BitArray");
        }

        public static bool GetBitArrayAt(this SerializedProperty property, uint bitIndex)
        {
            const string baseTypeName = "BitArray";
            string type = property.type;
            uint capacity;
            if (type.StartsWith(baseTypeName) && uint.TryParse(type.Substring(baseTypeName.Length), out capacity))
            {
                switch (capacity)
                {
                    case 8u: return Get8OnOneBitArray(property, bitIndex);
                    case 16u: return Get16OnOneBitArray(property, bitIndex);
                    case 32u: return Get32OnOneBitArray(property, bitIndex);
                    case 64u: return Get64OnOneBitArray(property, bitIndex);
                    case 128u: return Get128OnOneBitArray(property, bitIndex);
                    case 256u: return Get256OnOneBitArray(property, bitIndex);
                }
            }
            throw new ArgumentException("Trying to call Get on unknown BitArray");
        }

        public static void SetBitArrayAt(this SerializedProperty property, uint bitIndex, bool value)
        {
            var targets = property.serializedObject.targetObjects;
            if (targets.Length == 1)
            {
                const string baseTypeName = "BitArray";
                string type = property.type;
                uint capacity;
                if (type.StartsWith(baseTypeName) && uint.TryParse(type.Substring(baseTypeName.Length), out capacity))
                {
                    switch (capacity)
                    {
                        case 8u:    Set8OnOneBitArray(property, bitIndex, value);   return;
                        case 16u:   Set16OnOneBitArray(property, bitIndex, value);  return;
                        case 32u:   Set32OnOneBitArray(property, bitIndex, value);  return;
                        case 64u:   Set64OnOneBitArray(property, bitIndex, value);  return;
                        case 128u:  Set128OnOneBitArray(property, bitIndex, value); return;
                        case 256u:  Set256OnOneBitArray(property, bitIndex, value); return;
                    }
                }
                throw new ArgumentException("Trying to call Get on unknown BitArray");
            }
            else
            {
                string path = property.propertyPath;
                foreach (var target in targets)
                {
                    // Cannot do better at the moment as bitwise multi eddition only support UInt32 at the moment.
                    // Need C++ PR to do it correctly.
                    // Though, code should pass here only on modification so it is not high frequency operation.
                    // This workarround is ok meanwhile.
                    SerializedProperty isolatedProperty = new SerializedObject(target).FindProperty(path);

                    const string baseTypeName = "BitArray";
                    string type = isolatedProperty.type;
                    uint capacity;
                    if (type.StartsWith(baseTypeName) && uint.TryParse(type.Substring(baseTypeName.Length), out capacity))
                    {
                        switch (capacity)
                        {
                            case 8u:    Set8OnOneBitArray(isolatedProperty, bitIndex, value);   break;
                            case 16u:   Set16OnOneBitArray(isolatedProperty, bitIndex, value);  break;
                            case 32u:   Set32OnOneBitArray(isolatedProperty, bitIndex, value);  break;
                            case 64u:   Set64OnOneBitArray(isolatedProperty, bitIndex, value);  break;
                            case 128u:  Set128OnOneBitArray(isolatedProperty, bitIndex, value); break;
                            case 256u:  Set256OnOneBitArray(isolatedProperty, bitIndex, value); break;
                            default:
                                throw new ArgumentException("Trying to call Get on unknown BitArray");
                        }
                    }
                    
                    isolatedProperty.serializedObject.ApplyModifiedProperties();
                }
            }
            property.serializedObject.Update();
        }

        public static bool HasBitArrayMultipleDifferentValue(this SerializedProperty property, uint bitIndex)
        {
            const string baseTypeName = "BitArray";
            string type = property.type;
            uint capacity;
            if (type.StartsWith(baseTypeName) && uint.TryParse(type.Substring(baseTypeName.Length), out capacity))
            {
                switch (capacity)
                {
                    case 8u:
                    case 16u:
                    case 32u:
                        if (bitIndex >= capacity)
                            throw new IndexOutOfRangeException("Index out of bound in BitArray" + capacity);
                        return (HasMultipleDifferentValuesBitwise(property.FindPropertyRelative("data")) & (1 << (int)bitIndex)) != 0;
                    case 64u:
                        return HasBitArrayMultipleDifferentValue64(property.FindPropertyRelative("data"), bitIndex);
                    case 128u:
                        return bitIndex < 64u
                            ? HasBitArrayMultipleDifferentValue64(property.FindPropertyRelative("data1"), bitIndex)
                            : HasBitArrayMultipleDifferentValue64(property.FindPropertyRelative("data2"), bitIndex - 64u);
                    case 256u:
                        return bitIndex < 128u
                            ? bitIndex < 64u
                                ? HasBitArrayMultipleDifferentValue64(property.FindPropertyRelative("data1"), bitIndex)
                                : HasBitArrayMultipleDifferentValue64(property.FindPropertyRelative("data2"), bitIndex - 64u)
                            : bitIndex < 192u
                                ? HasBitArrayMultipleDifferentValue64(property.FindPropertyRelative("data3"), bitIndex - 128u)
                                : HasBitArrayMultipleDifferentValue64(property.FindPropertyRelative("data4"), bitIndex - 192u);
                }
            }
            throw new ArgumentException("Trying to call Get on unknown BitArray");
        }

        static bool HasBitArrayMultipleDifferentValue64(SerializedProperty property, uint bitIndex)
        {
            int length = property.serializedObject.targetObjects.Length;
            if (length < 2)
                return false;

            if (bitIndex >= 64u)
                throw new IndexOutOfRangeException("Index out of bound in BitArray" + GetBitArrayCapacity(property));

            string path = property.propertyPath;
            ulong mask = 1uL << (int)bitIndex;
            var objects = property.serializedObject.targetObjects;
            bool value = ((ulong)new SerializedObject(objects[0]).FindProperty(path).longValue & mask) != 0uL;
            for (int i = 1; i < length; ++i)
            {
                if ((((ulong)new SerializedObject(objects[i]).FindProperty(path).longValue & mask) != 0uL) ^ value)
                    return true;
            }
            return false;
        }


        // The remaining only handle SerializedProperty on omly ONE BitArray
        // Multi-edition should be handled before this.

        static bool Get8OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex)
            => BitArrayUtilities.Get8(bitIndex, (byte)propertyOnOneBitArray.FindPropertyRelative("data").intValue);
        static bool Get16OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex)
            => BitArrayUtilities.Get16(bitIndex, (ushort)propertyOnOneBitArray.FindPropertyRelative("data").intValue);
        static bool Get32OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex)
            => BitArrayUtilities.Get32(bitIndex, (uint)propertyOnOneBitArray.FindPropertyRelative("data").intValue);
        static bool Get64OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex)
            => BitArrayUtilities.Get64(bitIndex, (ulong)propertyOnOneBitArray.FindPropertyRelative("data").longValue);
        static bool Get128OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex)
            => BitArrayUtilities.Get128(
                bitIndex,
                (ulong)propertyOnOneBitArray.FindPropertyRelative("data1").longValue,
                (ulong)propertyOnOneBitArray.FindPropertyRelative("data2").longValue);
        static bool Get256OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex)
            => BitArrayUtilities.Get256(
                bitIndex,
                (ulong)propertyOnOneBitArray.FindPropertyRelative("data1").longValue,
                (ulong)propertyOnOneBitArray.FindPropertyRelative("data2").longValue,
                (ulong)propertyOnOneBitArray.FindPropertyRelative("data3").longValue,
                (ulong)propertyOnOneBitArray.FindPropertyRelative("data4").longValue);

        static void Set8OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex, bool value)
        {
            byte versionedData = (byte)propertyOnOneBitArray.FindPropertyRelative("data").intValue;
            BitArrayUtilities.Set8(bitIndex, ref versionedData, value);
            propertyOnOneBitArray.FindPropertyRelative("data").intValue = versionedData;
        }
        static void Set16OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex, bool value)
        {
            ushort versionedData = (ushort)propertyOnOneBitArray.FindPropertyRelative("data").intValue;
            BitArrayUtilities.Set16(bitIndex, ref versionedData, value);
            propertyOnOneBitArray.FindPropertyRelative("data").intValue = versionedData;
        }
        static void Set32OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex, bool value)
        {
            int versionedData = propertyOnOneBitArray.FindPropertyRelative("data").intValue;
            uint trueData;
            unsafe
            {
                trueData = *(uint*)(&versionedData);
            }
            BitArrayUtilities.Set32(bitIndex, ref trueData, value);
            unsafe
            {
                versionedData = *(int*)(&trueData);
            }
            propertyOnOneBitArray.FindPropertyRelative("data").intValue = versionedData;
        }
        static void Set64OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex, bool value)
        {
            long versionedData = propertyOnOneBitArray.FindPropertyRelative("data").longValue;
            ulong trueData;
            unsafe
            {
                trueData = *(ulong*)(&versionedData);
            }
            BitArrayUtilities.Set64(bitIndex, ref trueData, value);
            unsafe
            {
                versionedData = *(long*)(&trueData);
            }
            propertyOnOneBitArray.FindPropertyRelative("data").longValue = versionedData;
        }
        static void Set128OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex, bool value)
        {
            long versionedData1 = propertyOnOneBitArray.FindPropertyRelative("data1").longValue;
            long versionedData2 = propertyOnOneBitArray.FindPropertyRelative("data2").longValue;
            ulong trueData1;
            ulong trueData2;
            unsafe
            {
                trueData1 = *(ulong*)(&versionedData1);
                trueData2 = *(ulong*)(&versionedData2);
            }
            BitArrayUtilities.Set128(bitIndex, ref trueData1, ref trueData2, value);
            unsafe
            {
                versionedData1 = *(long*)(&trueData1);
                versionedData2 = *(long*)(&trueData2);
            }
            propertyOnOneBitArray.FindPropertyRelative("data1").longValue = versionedData1;
            propertyOnOneBitArray.FindPropertyRelative("data2").longValue = versionedData2;
        }
        static void Set256OnOneBitArray(SerializedProperty propertyOnOneBitArray, uint bitIndex, bool value)
        {
            long versionedData1 = propertyOnOneBitArray.FindPropertyRelative("data1").longValue;
            long versionedData2 = propertyOnOneBitArray.FindPropertyRelative("data2").longValue;
            long versionedData3 = propertyOnOneBitArray.FindPropertyRelative("data3").longValue;
            long versionedData4 = propertyOnOneBitArray.FindPropertyRelative("data4").longValue;
            ulong trueData1;
            ulong trueData2;
            ulong trueData3;
            ulong trueData4;
            unsafe
            {
                trueData1 = *(ulong*)(&versionedData1);
                trueData2 = *(ulong*)(&versionedData2);
                trueData3 = *(ulong*)(&versionedData3);
                trueData4 = *(ulong*)(&versionedData4);
            }
            BitArrayUtilities.Set256(bitIndex, ref trueData1, ref trueData2, ref trueData3, ref trueData4, value);
            unsafe
            {
                versionedData1 = *(long*)(&trueData1);
                versionedData2 = *(long*)(&trueData2);
                versionedData3 = *(long*)(&trueData3);
                versionedData4 = *(long*)(&trueData4);
            }
            propertyOnOneBitArray.FindPropertyRelative("data1").longValue = versionedData1;
            propertyOnOneBitArray.FindPropertyRelative("data2").longValue = versionedData2;
            propertyOnOneBitArray.FindPropertyRelative("data3").longValue = versionedData3;
            propertyOnOneBitArray.FindPropertyRelative("data4").longValue = versionedData4;
        }
    }
}
