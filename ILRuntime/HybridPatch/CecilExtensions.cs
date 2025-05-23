﻿using ILRuntime.Mono.Cecil;
using ILRuntime.Mono.Cecil.Cil;
using ILRuntime.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ILRuntime.Hybrid
{
    internal static class CecilExtensions
    {
        public static void FixClosureNameConsistency(this ModuleDefinition module)
        {
            MD5 md5 = MD5.Create();
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);

            if (module.HasTypes)
            {
                foreach (var i in module.Types)
                {
                    i.FixClosureNameConsistency(false, bw, md5);
                }
            }
        }

        public static void FixClosureNameConsistency(this TypeDefinition type, bool forceInclude, BinaryWriter bw, MD5 md5)
        {
            bool shouldInclude = forceInclude || type.ShouldIncludeInPatch();
            if (shouldInclude)
            {
                bool isClosure = type.IsClosureType();
                bool needMemberAlias = false;
                if (isClosure)
                {
                    if (type.Name.Contains("<>c__DisplayClass"))
                    {
                        type.Name = type.GetClosureAliasName(type.Name, "<>c__DisplayClass", bw, md5);
                    }
                    else
                        needMemberAlias = true;

                    if (needMemberAlias)
                    {
                        foreach (var f in type.Fields)
                        {
                            if (f.Name.Contains("__"))
                            {
                                f.Name = type.GetClosureAliasName(f.Name, "__", bw, md5);
                            }
                        }
                        foreach (var m in type.Methods)
                        {
                            if (m.Name.Contains("__"))
                            {
                                m.Name = type.GetClosureAliasName(m.Name, "__", bw, md5);
                            }
                        }
                    }
                }
                if (type.HasNestedTypes)
                {   
                    foreach (var i in type.NestedTypes)
                    {
                        i.FixClosureNameConsistency(shouldInclude, bw, md5);
                    }
                }
            }
        }

        static string attributeName = typeof(ILRuntimePatchAttribute).FullName;

        public static bool ShouldIncludeInPatch(this TypeDefinition type)
        {
            bool shouldInclude = false;
            if(type.HasCustomAttributes)
            {
                foreach (var attr in type.CustomAttributes)
                {
                    if (attr.AttributeType.FullName == attributeName)
                    {
                        shouldInclude = true;
                        break;
                    }
                }
            }
            return shouldInclude;
        }

        public static string GetClosureAliasName(this TypeDefinition type, string name, string identifier, BinaryWriter bw, MD5 md5)
        {
            int startIdx = name.IndexOf(identifier);
            string heading = name.Substring(0, startIdx);
            name = name.Substring(startIdx);
            var dt = type.DeclaringType;
            string trailing = name.Replace(identifier, "");
            string[] token = trailing.Split('_');
            int mIdx = int.Parse(token[0]);
            var mi = dt.Methods[mIdx - dt.Fields.Count];
            return $"{heading}{identifier}{mi.ComputeHash(bw, md5, false).Substring(0, 6)}_{token[1]}";
        }
        public static bool IsClosureType(this TypeDefinition type)
        {
            if (type.IsNested)
            {
                if (type.Name.StartsWith("<>c"))
                {
                    if (type.HasCustomAttributes)
                    {
                        foreach(var i in type.CustomAttributes)
                        {
                            if (i.AttributeType.Name == "CompilerGeneratedAttribute")
                                return true;
                        }
                    }
                    return false;
                }
                else
                    return false;
            }
            else
                return false;
        }

        public static void GetTypeNames(this TypeDefinition type, out string namesp, out string name)
        {
            if (type.IsNested)
            {
                var dt = type.DeclaringType;
                GetTypeNames(dt, out namesp, out var tn);
                name = tn + "+" + type.Name;
            }
            else
            {
                namesp = type.Namespace;
                name = type.Name;
            }
        }
        public static string GetSafeFullNames(this TypeReference type)
        {
            type.Resolve().GetTypeNames(out var namesp, out var name);
            if (!string.IsNullOrEmpty(namesp))
                return $"{namesp}.{name}";
            else
                return name;
        }
        public static MethodReference GetMethod(this TypeReference type, string name)
        {
            foreach (var i in type.Resolve().Methods)
            {
                if (i.Name == name)
                    return i;
            }
            return null;
        }
        public static GenericInstanceMethod MakeGenericInstanceMethod(this MethodReference self, params TypeReference[] arguments)
        {
            if (self == null)
                throw new ArgumentNullException("self");
            if (arguments == null)
                throw new ArgumentNullException("arguments");
            if (arguments.Length == 0)
                throw new ArgumentException();
            if (self.GenericParameters.Count != arguments.Length)
                throw new ArgumentException();

            var instance = new GenericInstanceMethod(self);

            foreach (var argument in arguments)
                instance.GenericArguments.Add(argument);

            return instance;
        }

        public static GenericInstanceType MakeGenericInstanceType(this TypeReference self, params TypeReference[] arguments)
        {
            if (self == null)
                throw new ArgumentNullException("self");
            if (arguments == null)
                throw new ArgumentNullException("arguments");
            if (arguments.Length == 0)
                throw new ArgumentException();
            if (self.GenericParameters.Count != arguments.Length)
                throw new ArgumentException();

            var instance = new GenericInstanceType(self);

            foreach (var argument in arguments)
                instance.GenericArguments.Add(argument);

            return instance;
        }

        public static int FindGenericArgument(this GenericInstanceType self, string name)
        {
            var def = self.Resolve();
            for(int i = 0; i < def.GenericParameters.Count; i++)
            {
                if (def.GenericParameters[i].Name == name)
                    return i;
            }
            return -1;
        }

        public static MethodReference GetMethod(this GenericInstanceType self, MethodReference method)
        {
            if (self.Resolve() != method.DeclaringType)
                throw new NotSupportedException();
            var result = new MethodReference(method.Name, method.ReturnType)
            {
                DeclaringType = self,
                HasThis = method.HasThis,
                ExplicitThis = method.ExplicitThis,
                CallingConvention = method.CallingConvention,
            };
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                var p = method.Parameters[i];
                TypeReference tr = p.ParameterType;
                result.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, tr));
            }
            return result;
        }

        public static void AppendLoadArgument(this ILProcessor processor, int paramIdx, Instruction first)
        {
            switch (paramIdx)
            {
                case 0:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldarg_0));
                    break;
                case 1:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldarg_1));
                    break;
                case 2:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldarg_2));
                    break;
                case 3:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldarg_3));
                    break;
                default:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldarg, paramIdx));
                    break;
            }
        }

        public static void AppendStoreArgument(this ILProcessor processor, ModuleDefinition module, ReflectionReferences reflection, VariableDefinition invokeCtx, ParameterDefinition param,int refIdx, int paramIdx, Instruction first)
        {
            var pt = ((TypeSpecification)param.ParameterType).ElementType;
            if (pt == module.TypeSystem.Int32 || pt == module.TypeSystem.Char || pt == module.TypeSystem.Boolean)
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32ByIndexMethod));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_I4));
            }
            else if (pt == module.TypeSystem.UInt32)
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32ByIndexMethod));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_I4));
            }
            else if (pt == module.TypeSystem.Int16)
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32ByIndexMethod));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_I2));
            }
            else if (pt == module.TypeSystem.UInt16)
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32ByIndexMethod));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_I2));
            }
            else if (pt == module.TypeSystem.Byte)
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32ByIndexMethod));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_I1));
            }
            else if (pt == module.TypeSystem.SByte)
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32ByIndexMethod));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_I1));
            }
            else if (pt == module.TypeSystem.Int64)
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt64ByIndexMethod));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_I8));
            }
            else if (pt == module.TypeSystem.UInt64)
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt64ByIndexMethod));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_I8));
            }
            else if (pt == module.TypeSystem.Single)
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadFloatByIndexMethod));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_R4));
            }
            else if (pt == module.TypeSystem.Double)
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadDoubleByIndexMethod));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_R8));
            }
            else if (!pt.ContainsGenericParameter && pt.Resolve().IsEnum)
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32ByIndexMethod));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_I4));
            }
            else
            {
                processor.AppendLoadArgument(paramIdx, first);
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                processor.AppendLdc(first, refIdx);
                if (pt.ContainsGenericParameter || pt.IsValueType)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.GetReadResultByIndexMethod(pt)));
                else
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.GetReadObjectByIndexMethod(pt)));
                AppendInstruction(processor, first, processor.Create(OpCodes.Stind_Ref));
            }
        }

        public static void AppendReadReturnValue(this ILProcessor processor, ModuleDefinition module, TypeReference returnType, ReflectionReferences reflection, VariableDefinition invokeCtx, Instruction first)
        {
            if (returnType != module.TypeSystem.Void)
            {
                AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
                if (returnType == module.TypeSystem.Int32 || returnType == module.TypeSystem.Char || returnType == module.TypeSystem.Boolean)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32Method));
                }
                else if (returnType == module.TypeSystem.UInt32)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32Method));
                    AppendInstruction(processor, first, processor.Create(OpCodes.Conv_U4));
                }
                else if (returnType == module.TypeSystem.Int16)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32Method));
                    AppendInstruction(processor, first, processor.Create(OpCodes.Conv_I2));
                }
                else if (returnType == module.TypeSystem.UInt16)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32Method));
                    AppendInstruction(processor, first, processor.Create(OpCodes.Conv_U2));
                }
                else if (returnType == module.TypeSystem.Byte)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32Method));
                    AppendInstruction(processor, first, processor.Create(OpCodes.Conv_U1));
                }
                else if (returnType == module.TypeSystem.SByte)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32Method));
                    AppendInstruction(processor, first, processor.Create(OpCodes.Conv_I1));
                }
                else if (returnType == module.TypeSystem.Int64)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt64Method));
                }
                else if (returnType == module.TypeSystem.UInt64)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt64Method));
                    AppendInstruction(processor, first, processor.Create(OpCodes.Conv_U8));
                }
                else if (returnType == module.TypeSystem.Single)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadFloatMethod));
                }
                else if (returnType == module.TypeSystem.Double)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadDoubleMethod));
                }
                else if (!returnType.ContainsGenericParameter && returnType.Resolve().IsEnum)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.ReadInt32Method));
                }
                else
                {
                    if(returnType.ContainsGenericParameter || returnType.IsValueType)
                        AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.GetReadResultMethod(returnType)));
                    else
                        AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.GetReadObjectMethod(returnType)));
                }
            }
        }

        public static void AppendPushArgument(this ILProcessor processor, ModuleDefinition module, ReflectionReferences reflection, VariableDefinition invokeCtx, ParameterDefinition param, int paramIdx, Instruction first)
        {
            AppendInstruction(processor, first, processor.Create(OpCodes.Ldloca, invokeCtx));
            bool isByref = param.ParameterType.IsByReference;
            var pt = isByref ? ((TypeSpecification)param.ParameterType).ElementType : param.ParameterType;
            if (pt == module.TypeSystem.Int32 || pt == module.TypeSystem.Char || pt == module.TypeSystem.Boolean)
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_I4));
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushInt32Method));
            }
            else if (pt == module.TypeSystem.UInt32)
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_U4));
                AppendInstruction(processor, first, processor.Create(OpCodes.Conv_I4));
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushInt32Method));
            }
            else if (pt == module.TypeSystem.Int16)
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_I2));
                AppendInstruction(processor, first, processor.Create(OpCodes.Conv_I4));
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushInt32Method));
            }
            else if (pt == module.TypeSystem.UInt16)
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_U2));
                AppendInstruction(processor, first, processor.Create(OpCodes.Conv_I4));
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushInt32Method));
            }
            else if (pt == module.TypeSystem.Byte)
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_U1));
                AppendInstruction(processor, first, processor.Create(OpCodes.Conv_I4));
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushInt32Method));
            }
            else if (pt == module.TypeSystem.SByte)
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_I1));
                AppendInstruction(processor, first, processor.Create(OpCodes.Conv_I4));
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushInt32Method));
            }
            else if (pt == module.TypeSystem.Int64)
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_I8));
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushInt64Method));
            }
            else if (pt == module.TypeSystem.UInt64)
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_I8));
                    AppendInstruction(processor, first, processor.Create(OpCodes.Conv_U8));
                }
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushInt64Method));
            }
            else if (pt == module.TypeSystem.Single)
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_R4));
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushInt64Method));
            }
            else if (pt == module.TypeSystem.Double)
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_R8));
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushInt64Method));
            }
            else if(!pt.ContainsGenericParameter && pt.Resolve().IsEnum)
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_I4));
                AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushInt32Method));
            }
            else
            {
                AppendLoadArgument(processor, paramIdx, first);
                if (isByref)
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldind_Ref));
                if (pt.ContainsGenericParameter)
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.GetPushParameterMethod(pt)));
                }
                else
                {
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_1));
                    AppendInstruction(processor, first, processor.Create(OpCodes.Call, reflection.PushObjectMethod));
                }
            }
        }

        public static void AppendLdc(this ILProcessor processor, Instruction first, int val)
        {
            switch (val)
            {
                case 0:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_0));
                    break;
                case 1:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_1));
                    break;
                case 2:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_2));
                    break;
                case 3:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_3));
                    break;
                case 4:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_4));
                    break;
                case 5:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_5));
                    break;
                case 6:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_6));
                    break;
                case 7:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_7));
                    break;
                case 8:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_8));
                    break;
                case -1:
                    AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_M1));
                    break;
                default:
                    if (val < 128 && val >= -128)
                        AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4_S, val));
                    else
                        AppendInstruction(processor, first, processor.Create(OpCodes.Ldc_I4, val));
                    break;
            }
        }

        public static void AppendInstruction(this ILProcessor processor, Instruction before, Instruction instruction)
        {
            if (before != null)
                processor.InsertBefore(before, instruction);
            else
                processor.Append(instruction);
        }
    }
}
