﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ILRuntime.CLR.Method;
using ILRuntimeTest.TestBase;
using AppDomain = ILRuntime.Runtime.Enviorment.AppDomain;

namespace ILRuntimeTest.Test
{
    public enum TestResults
    {
        None,
        Pass,
        Failed,
        Ignored,
    }
    public abstract class BaseTestUnit : ITestable
    {
        protected AppDomain App;
        protected string AssemblyName;
        protected string TypeName;
        protected string MethodName;
        protected TestResults Pass;
        protected bool IsToDo;
        protected StringBuilder Message = null;//= new StringBuilder();

        public string TestName { get { return TypeName + "." + MethodName; } }

        #region 接口方法

        public bool Init(string fileName)
        {
            AssemblyName = fileName;
            if (!File.Exists(AssemblyName))
                return false;
            using (var fs = new System.IO.FileStream(AssemblyName, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            {
                App = new ILRuntime.Runtime.Enviorment.AppDomain();
                var path = System.IO.Path.GetDirectoryName(AssemblyName);
                var name = System.IO.Path.GetFileNameWithoutExtension(AssemblyName);
                using (var fs2 = new System.IO.FileStream(string.Format("{0}\\{1}.pdb", path, name), System.IO.FileMode.Open))
                    App.LoadAssembly(fs, fs2, new ILRuntime.Mono.Cecil.Pdb.PdbReaderProvider());
            }

            return true;
        }

        public bool Init(ILRuntime.Runtime.Enviorment.AppDomain app)
        {
            if (app == null)
                return false;

            App = app;
            return true;
        }

        public bool Init(ILRuntime.Runtime.Enviorment.AppDomain app, string type, string method)
        {
            if (app == null)
                return false;

            TypeName = type;
            MethodName = method;

            App = app;
            return true;
        }

        //需要子类去实现
        public abstract void Run(bool skipPerformance = false);

        public abstract bool Check();

        public abstract TestResultInfo CheckResult();

        #endregion

        #region 常用工具方法

        public Object getInstance(string type)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// call Method with no params and no return value;
        /// </summary>
        /// <param name="Instance">Instacne, if it is null means static method,else means instance method</param>
        /// <param name="type">TypeName ,eg "Namespace.ClassType"</param>
        /// <param name="method">MethodName</param>
        public void Invoke(Object Instance, string type, string method)
        {
            Message = new StringBuilder();
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            App.Invoke(type, method, null); //InstanceTest
            sw.Stop();
            Message.AppendLine("Elappsed Time:" + sw.ElapsedMilliseconds + "ms\n");
        }

        public void Invoke(string type, string method, bool skipPerformance)
        {
            Message = new StringBuilder();
            Type expectingEx = null;
            IsToDo = false;
            try
            {
                var sw = new System.Diagnostics.Stopwatch();
                Console.WriteLine("Invoking {0}.{1}", type, method);
                sw.Start();
                var im = App.LoadedTypes[type].GetMethod(method, 0) as ILMethod;
                var attributes = im.ReflectionMethodInfo.GetCustomAttributes(typeof(ILRuntimeTestAttribute), false);
                if (attributes.Length > 0)
                {
                    var attr = attributes[0] as ILRuntimeTestAttribute;
                    if (attr.Ignored)
                    {
                        Pass = TestResults.Ignored;
                        return;
                    }
                    IsToDo = attr.IsToDo;
                    if(attr.IsPerformanceTest && skipPerformance)
                    {
                        Pass = TestResults.Ignored;
                        return;
                    }
                    expectingEx = attr.ExpectException;
                }
                var res = App.Invoke(im, null); //InstanceTest
                sw.Stop();
                if (res != null)
                    Message.AppendLine("Return:" + res);
                Message.AppendLine("Elappsed Time:" + sw.ElapsedMilliseconds + "ms\n");
                if (expectingEx != null)
                {
                    Message.AppendLine($"Test ran completed without exception, but expecting {expectingEx}");
                    Pass = TestResults.Failed;
                }
                else
                    Pass = TestResults.Pass;
            }
            catch (ILRuntime.Runtime.Intepreter.ILRuntimeException e)
            {
                if (e.GetInnerException() == null || e.GetInnerException().GetType() != expectingEx)
                {
                    Message.AppendLine(e.ToString());
                    if (e.InnerException != null)
                        Message.AppendLine(e.InnerException.ToString());
                    Pass = TestResults.Failed;
                }
                else
                    Pass = TestResults.Pass;
            }
            catch (Exception e)
            {
                if (e.GetType() != expectingEx)
                {
                    Message.AppendLine(e.ToString());
                    Pass = TestResults.Failed;
                }
                else
                    Pass = TestResults.Pass;
            }
        }

        ////无返回值
        //void Invoke<T>(Object Instance, string type, string method, T param)
        //{
        //    throw new NotImplementedException();
        //}

        //void Invoke<T1, T2>(Object Instance, string type, string method, T1 param1, T2 param2)
        //{
        //    throw new NotImplementedException();
        //}

        //void Invoke<T1, T2, T3>(Object Instance, string type, string method, T1 param1, T2 param2, T3 param3)
        //{
        //    throw new NotImplementedException();
        //}

        //void Invoke<T1, T2, T3, T4>(Object Instance, string type, string method, T1 param1, T2 param2, T3 param3, T4 param4)
        //{
        //    throw new NotImplementedException();
        //}

        //void Invoke<T1, T2, T3, T4, T5>(Object Instance, string type, string method, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5)
        //{
        //    throw new NotImplementedException();
        //}

        ////有返回值
        //T Invoke<T>(Object Instance, string type, string method)
        //{
        //    throw new NotImplementedException();
        //}

        //T Invoke<T, T1, T2>(Object Instance, string type, string method, T1 param1, T2 param2)
        //{
        //    throw new NotImplementedException();
        //}

        //T Invoke<T, T1, T2, T3>(Object Instance, string type, string method, T1 param1, T2 param2, T3 param3)
        //{
        //    throw new NotImplementedException();
        //}

        //T Invoke<T, T1, T2, T3, T4>(Object Instance, string type, string method, T1 param1, T2 param2, T3 param3, T4 param4)
        //{
        //    throw new NotImplementedException();
        //}

        //T Invoke<T, T1, T2, T3, T4, T5>(Object Instance, string type, string method, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5)
        //{
        //    throw new NotImplementedException();
        //}

        //protected bool AssertToBe<T>(T assertValue, T result)
        //{
        //    throw new NotImplementedException();
        //}

        //protected bool AssertNotToBe<T>(T errorValue, T result)
        //{
        //    throw new NotImplementedException();
        //}

        //protected void Assert(bool expression, string errorLog)
        //{
        //    throw new NotImplementedException();
        //}

        //protected void Assert(bool expression, Action<bool> resAction)
        //{
        //    throw new NotImplementedException();
        //}

        #endregion

    }
}
