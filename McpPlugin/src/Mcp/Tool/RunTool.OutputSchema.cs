/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Utils;
using com.IvanMurzak.ReflectorNet;
using com.IvanMurzak.ReflectorNet.Utils;

namespace com.IvanMurzak.McpPlugin
{
    public partial class RunTool
    {
        protected override JsonNode? CreateOutputSchema(Reflector reflector, MethodInfo methodInfo)
        {
            var returnType = methodInfo.ReturnType;
            var isNullable = MethodUtils.IsReturnTypeNullable(methodInfo);

            // Ignore void, Task, and ValueTask - these have no return value
            if (returnType == typeof(void) ||
                returnType == typeof(Task) ||
                returnType == typeof(ValueTask))
                return null;

            // Unwrap Task<T> or ValueTask<T>
            if (returnType.IsGenericType && (returnType.GetGenericTypeDefinition() == typeof(Task<>) ||
                                             returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            {
                returnType = returnType.GetGenericArguments()[0];
            }

            // Unwrap Nullable<T>
            var nullableUnderlyingType = Nullable.GetUnderlyingType(returnType);
            if (nullableUnderlyingType != null)
            {
                returnType = nullableUnderlyingType;
                isNullable = true;
            }

            if (returnType.IsGenericType)
            {
                // Unwrap ResponseCallValueTool<T>
                if (returnType.GetGenericTypeDefinition() == typeof(ResponseCallValueTool<>))
                {
                    var genericArg = returnType.GetGenericArguments()[0];

                    var types = new (Type type, string name, string? description, bool required)[]
                    {
                        (
                            type: genericArg,
                            name: JsonSchema.Result,
                            description: null,
                            required: !isNullable
                        )
                    };
                    var schema = reflector.JsonSchema.GenerateSchema(reflector, types, justRef: false, defines: null);
                    return JsonSchemaUtils.FixSerializedMemberSchema(schema);
                }

                // Ignore ResponseCallTool and its subclasses
                if (returnType == typeof(ResponseCallTool) || returnType.IsSubclassOf(typeof(ResponseCallTool)))
                    return null;
            }
            else
            {
                // Ignore ResponseCallTool and its subclasses
                if (returnType == typeof(ResponseCallTool) || returnType.IsSubclassOf(typeof(ResponseCallTool)))
                    return null;
            }

            return JsonSchemaUtils.FixSerializedMemberSchema(base.CreateOutputSchema(reflector, methodInfo));
        }
    }
}
