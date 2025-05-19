// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks.AssemblyDependency
{
    internal static class RarTaskParameters
    {
        private static PropertyGetter[]? s_reflectedInputGetters;
        private static PropertySetter[]? s_reflectedInputSetters;
        private static PropertyGetter[]? s_reflectedOutputGetters;
        private static PropertySetter[]? s_reflectedOutputSetters;

        internal static Dictionary<string, TaskParameter> GetTaskInputs(ResolveAssemblyReference rarTask)
        {
            if (s_reflectedInputGetters == null)
            {
                InitReflectedParameters();
            }

            return GetTaskParameters(rarTask, s_reflectedInputGetters!);
        }

        internal static Dictionary<string, TaskParameter> GetTaskOutputs(ResolveAssemblyReference rarTask)
        {
            if (s_reflectedOutputGetters == null)
            {
                InitReflectedParameters();
            }

            return GetTaskParameters(rarTask, s_reflectedOutputGetters!);
        }

        internal static void SetTaskInputs(ResolveAssemblyReference rarTask, Dictionary<string, TaskParameter> parameters)
        {
            if (s_reflectedInputSetters == null)
            {
                InitReflectedParameters();
            }

            SetTaskParameters(rarTask, parameters, s_reflectedInputSetters!);
        }

        internal static void SetTaskOutputs(ResolveAssemblyReference rarTask, Dictionary<string, TaskParameter> parameters)
        {
            if (s_reflectedOutputSetters == null)
            {
                InitReflectedParameters();
            }

            SetTaskParameters(rarTask, parameters, s_reflectedOutputSetters!);
        }

        /// <summary>
        /// Creates a dictionary mapping each property name to its current value on this instance.
        /// Use to serialize the RAR task without manually keeping input / output parameters in-sync.
        /// </summary>
        private static Dictionary<string, TaskParameter> GetTaskParameters(ResolveAssemblyReference rarTask, PropertyGetter[] getters)
        {
            Dictionary<string, TaskParameter> taskParameters = new(StringComparer.OrdinalIgnoreCase);

            foreach (PropertyGetter getter in getters)
            {
                object value = getter.GetValue(rarTask);

                // Avoid serializing empty parameters.
                if (value == null || (value is Array array && array.Length == 0))
                {
                    continue;
                }

                taskParameters[getter.Name] = new TaskParameter(value);
            }

            return taskParameters;
        }

        /// <summary>
        /// Sets each property on this instance to the value of the corresponding task parameter.
        /// Use to hydrate the RAR task without manually keeping input / output parameters in-sync.
        /// </summary>
        private static void SetTaskParameters(ResolveAssemblyReference rarTask, Dictionary<string, TaskParameter> parameters, PropertySetter[] setters)
        {
            foreach (PropertySetter setter in setters)
            {
                if (parameters.TryGetValue(setter.Name, out TaskParameter? parameter))
                {
                    setter.SetValue(rarTask, parameter.WrappedParameter);
                }
            }
        }

        internal static void InitReflectedParameters()
        {
            List<PropertyGetter> inputGetters = [];
            List<PropertySetter> inputSetters = [];
            List<PropertyGetter> outputGetters = [];
            List<PropertySetter> outputSetters = [];

            // Setup common expression roots.
            Type rarType = typeof(ResolveAssemblyReference);
            ParameterExpression sourceParameter = Expression.Parameter(rarType);
            Expression instanceExpression = Expression.Convert(sourceParameter, rarType);

            // Exclude CopyLocalFiles since it is a list of references - otherwise we'll end up with duplicated task item instances.
            // We reconstruct this on our own using item metadata.
            string copyLocalFilesName = nameof(ResolveAssemblyReference.CopyLocalFiles);

            PropertyInfo[] properties = rarType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (PropertyInfo property in properties)
            {
                MethodInfo? getMethod = property.GetGetMethod();
                MethodInfo? setMethod = property.GetSetMethod(nonPublic: true);

                if (getMethod != null && setMethod != null)
                {
                    if (setMethod.IsPublic)
                    {
                        inputGetters.Add(CompileGetter(sourceParameter, instanceExpression, property, getMethod));
                        inputSetters.Add(CompileSetter(sourceParameter, instanceExpression, property, setMethod));
                    }
                    else if (property.GetCustomAttribute<OutputAttribute>() != null
                        && !property.Name.Equals(copyLocalFilesName, StringComparison.Ordinal))
                    {
                        outputGetters.Add(CompileGetter(sourceParameter, instanceExpression, property, getMethod));
                        outputSetters.Add(CompileSetter(sourceParameter, instanceExpression, property, setMethod));
                    }
                }
            }

            s_reflectedInputGetters = [.. inputGetters];
            s_reflectedInputSetters = [.. inputSetters];
            s_reflectedOutputGetters = [.. outputGetters];
            s_reflectedOutputSetters = [.. outputSetters];
        }

        private static PropertyGetter CompileGetter(ParameterExpression sourceExpression, Expression instanceExpression, PropertyInfo property, MethodInfo getMethod)
        {
            Expression returnExpression = Expression.Call(instanceExpression, getMethod);
            if (!property.PropertyType.IsClass)
            {
                returnExpression = Expression.Convert(returnExpression, typeof(object));
            }

            Delegate getter = Expression.Lambda(returnExpression, sourceExpression).Compile();

            return new PropertyGetter(property.Name, (Func<ResolveAssemblyReference, object>)getter);
        }

        private static PropertySetter CompileSetter(ParameterExpression sourceExpression, Expression instanceExpression, PropertyInfo property, MethodInfo setMethod)
        {
            ParameterExpression propertyValueParam = Expression.Parameter(typeof(object));
            Expression valueExpression = Expression.Convert(propertyValueParam, property.PropertyType);
            MethodCallExpression callExpression = Expression.Call(instanceExpression, setMethod, valueExpression);
            Delegate setter = Expression.Lambda(callExpression, sourceExpression, propertyValueParam).Compile();

            return new PropertySetter(property.Name, (Action<ResolveAssemblyReference, object>)setter);
        }

        private readonly struct PropertyGetter
        {
            public readonly string Name;
            public readonly Func<ResolveAssemblyReference, object> GetValue;

            internal PropertyGetter(string name, Func<ResolveAssemblyReference, object> getValue)
            {
                Name = name;
                GetValue = getValue;
            }
        }

        private readonly struct PropertySetter
        {
            public readonly string Name;
            public readonly Action<ResolveAssemblyReference, object> SetValue;

            internal PropertySetter(string name, Action<ResolveAssemblyReference, object> setValue)
            {
                Name = name;
                SetValue = setValue;
            }
        }
    }
}
