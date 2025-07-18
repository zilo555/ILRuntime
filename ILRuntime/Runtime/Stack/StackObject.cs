using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ILRuntime.CLR.TypeSystem;
using ILRuntime.Runtime.Enviorment;
using ILRuntime.Runtime.Intepreter;
#if DEBUG && !DISABLE_ILRUNTIME_DEBUG
using AutoList = System.Collections.Generic.List<object>;
#else
using AutoList = ILRuntime.Other.UncheckedList<object>;
#endif
namespace ILRuntime.Runtime.Stack
{
#pragma warning disable CS0660
#pragma warning disable CS0661
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct StackObject
    {
        public static StackObject Null = new StackObject() { ObjectType = ObjectTypes.Null, Value = -1, ValueLow = 0 };
        public ObjectTypes ObjectType;
        public int Value;
        public int ValueLow;

        public static bool operator ==(StackObject a, StackObject b)
        {
            return (a.ObjectType == b.ObjectType) && (a.Value == b.Value) && (a.ValueLow == b.ValueLow);
        }

        public static bool operator !=(StackObject a, StackObject b)
        {
            return (a.ObjectType != b.ObjectType) || (a.Value != b.Value) || (a.ValueLow == b.ValueLow);
        }

        [Obsolete]
        public static unsafe object ToObject(StackObject* esp, ILRuntime.Runtime.Enviorment.AppDomain appdomain, IList<object> mStack)
        {
            return ToObject(esp, appdomain, (AutoList)mStack);
        }
        //IL2CPP can't process esp->ToObject() properly, so I can only use static function for this
        public static unsafe object ToObject(StackObject* esp, ILRuntime.Runtime.Enviorment.AppDomain appdomain, AutoList mStack)
        {
            switch (esp->ObjectType)
            {
                case ObjectTypes.Integer:
                    return esp->Value;
                case ObjectTypes.Long:
                    {
                        return *(long*)&esp->Value;
                    }
                case ObjectTypes.Float:
                    {
                        return *(float*)&esp->Value;
                    }
                case ObjectTypes.Double:
                    {
                        return *(double*)&esp->Value;
                    }
                case ObjectTypes.Object:
                    return mStack[esp->Value];
                case ObjectTypes.FieldReference:
                    {
                        ILTypeInstance instance = mStack[esp->Value] as ILTypeInstance;
                        if (instance != null)
                        {
                            return instance[esp->ValueLow];
                        }
                        else
                        {
                            var obj = mStack[esp->Value];
                            IType t = null;
                            if (obj is CrossBindingAdaptorType)
                            {
                                t = appdomain.GetType(((CrossBindingAdaptor)((CrossBindingAdaptorType)obj).ILInstance.Type.FirstCLRBaseType).BaseCLRType);
                            }
                            else
                                t = appdomain.GetType(obj.GetType());

                            return ((CLRType)t).GetFieldValue(esp->ValueLow, obj);
                        }
                    }
                case ObjectTypes.ArrayReference:
                    {
                        Array instance = mStack[esp->Value] as Array;
                        return instance.GetValue(esp->ValueLow);
                    }
                case ObjectTypes.StaticFieldReference:
                    {
                        var t = appdomain.GetType(esp->Value);
                        if (t is CLR.TypeSystem.ILType)
                        {
                            CLR.TypeSystem.ILType type = (CLR.TypeSystem.ILType)t;
                            return type.StaticInstance[esp->ValueLow];
                        }
                        else
                        {
                            CLR.TypeSystem.CLRType type = (CLR.TypeSystem.CLRType)t;
                            return type.GetFieldValue(esp->ValueLow, null);
                        }
                    }
                case ObjectTypes.StackObjectReference:
                    {
                        return ToObject((ILIntepreter.ResolveReference(esp)), appdomain, mStack);
                    }
                case ObjectTypes.ValueTypeObjectReference:
                    {
                        StackObject* dst = ILIntepreter.ResolveReference(esp);
                        IType type = appdomain.GetTypeByIndex(dst->Value);
                        if (type is ILType)
                        {
                            ILType iltype = (ILType)type;
                            var ins = iltype.Instantiate(false);
                            for (int i = 0; i < dst->ValueLow; i++)
                            {
                                var addr = dst - (i + 1);
                                //ILIntepreter instance is only needed in clrbinding, in this case, it's pure value type declared inside ILRuntime, so it won't need it
                                //Adding a parameter in this widely used method would introduce unnecessary overhead
                                ins.AssignFromStack(i, addr, null, mStack);
                            }
                            return ins;
                        }
                        else
                        {
                            return ((CLRType)type).ValueTypeBinder.ToObject(dst, mStack);
                        }
                    }
                case ObjectTypes.Null:
                    return null;
                default:
                    throw new NotImplementedException();
            }
        }

        public unsafe static void Initialized(ref StackObject esp, int idx, IType fieldType, AutoList mStack)
        {
            if (fieldType.IsPrimitive)
            {
                esp = fieldType.DefaultObject;
            }
            else
            {
                if (fieldType.IsValueType)
                {
                    esp.ObjectType = ObjectTypes.Object;
                    esp.Value = idx;
                    if (fieldType is CLRType)
                    {
                        if (fieldType.TypeForCLR.IsEnum)
                        {
                            esp.ObjectType = ObjectTypes.Integer;
                            esp.Value = 0;
                            esp.ValueLow = 0;
                            mStack[idx] = null;
                        }
                        else
                            mStack[idx] = ((CLRType)fieldType).CreateDefaultInstance();
                    }
                    else
                    {
                        if (((ILType)fieldType).IsEnum)
                        {
                            esp.ObjectType = ObjectTypes.Integer;
                            esp.Value = 0;
                            esp.ValueLow = 0;
                            mStack[idx] = null;
                        }
                        else
                            mStack[idx] = ((ILType)fieldType).Instantiate();
                    }
                }
                else
                {
                    esp = Null;
                    mStack[idx] = null;
                }
            }
        }

        //IL2CPP can't process esp->Initialized() properly, so I can only use static function for this
        public unsafe static void Initialized(StackObject* esp, IType type)
        {
            if (type.IsPrimitive)
            {
                *esp = type.DefaultObject;
            }
            else if (type.IsEnum)
            {
                ILType ilType = type as ILType;
                if (ilType != null)
                {
                    Initialized(esp, ilType.FieldTypes[0]);
                }
                else
                {
                    Initialized(esp, ((CLRType)type).OrderedFieldTypes[0]);
                }
            }
            else
            {
                *esp = Null;
            }
        }
    }

    public enum ObjectTypes
    {
        Null,
        Integer,
        Long,
        Float,
        Double,
        StackObjectReference,//Value = pointer, 
        StaticFieldReference,
        ValueTypeDescriptor,
        ValueTypeObjectReference,
        Object,
        FieldReference,//Value = objIdx, ValueLow = fieldIdx
        ArrayReference,//Value = objIdx, ValueLow = elemIdx
    }
}
