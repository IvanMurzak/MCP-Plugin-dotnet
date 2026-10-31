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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Utils;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Provides functionality to execute methods dynamically, supporting both static and instance methods.
    /// Allows for parameter passing by position or by name, with support for default parameter values.
    /// </summary>
    public partial class RunTool : MethodWrapper, IRunTool
    {
        public string Name { get; private set; }
        /// <summary>Whether the tool is listed in <c>tools/list</c>. A hidden tool still runs through <c>tools/call</c>.</summary>
        public bool Visible { get; set; } = true;
        /// <summary>Old name of <see cref="Visible"/>.</summary>
        [Obsolete("Renamed to Visible" + VisibilityRename.Note)]
        public bool Enabled { get => Visible; set => Visible = value; }
        public McpToolType ToolType { get; protected set; } = McpToolType.Standard;
        /// <summary>Which plugin connections register this tool (from <see cref="AiToolAttribute.Target"/> / <see cref="AiToolTypeAttribute.Target"/>).</summary>
        public Common.ToolTarget Target { get; set; } = Common.ToolTarget.Both;
        public string? Title { get; protected set; }
        public bool? ReadOnlyHint { get; protected set; }
        public bool? DestructiveHint { get; protected set; }
        public bool? IdempotentHint { get; protected set; }
        public bool? OpenWorldHint { get; protected set; }

        /// <summary>
        /// Reads <see cref="AiSkillDescriptionAttribute"/> from the underlying method, if present.
        /// Used by <see cref="Skills.SkillFileGenerator"/> in place of <see cref="MethodWrapper.Description"/>
        /// when building the SKILL.md YAML <c>description:</c> field, AND copied onto the MCP
        /// <c>tools/list</c> entry as <c>_meta.skillDescription</c> — see <see cref="IRunTool.SkillDescription"/>.
        /// </summary>
        /// <remarks>
        /// Uses the plural <c>GetCustomAttributes</c> overload so that a method carrying BOTH
        /// <see cref="AiSkillDescriptionAttribute"/> and the legacy
        /// <see cref="McpPluginSkillDescriptionAttribute"/> subclass alias does not throw
        /// <see cref="System.Reflection.AmbiguousMatchException"/>.
        /// </remarks>
        public string? SkillDescription
            => Method?.GetCustomAttributes(typeof(AiSkillDescriptionAttribute), inherit: true)
                .Cast<AiSkillDescriptionAttribute>()
                .FirstOrDefault()?.Description;

        /// <summary>
        /// Reads <see cref="AiSkillBodyAttribute"/> from the underlying method, if present.
        /// Used by <see cref="Skills.SkillFileGenerator"/> to inject long-form markdown into the SKILL.md body
        /// between the description paragraph and the <c>## How to Call</c> section, AND copied onto the
        /// MCP <c>tools/list</c> entry as <c>_meta.skillBody</c> — see <see cref="IRunTool.SkillBody"/>.
        /// </summary>
        /// <remarks>
        /// Uses the plural <c>GetCustomAttributes</c> overload so that a method carrying BOTH
        /// <see cref="AiSkillBodyAttribute"/> and the legacy <see cref="McpPluginSkillBodyAttribute"/>
        /// subclass alias does not throw <see cref="System.Reflection.AmbiguousMatchException"/>.
        /// </remarks>
        public string? SkillBody
            => Method?.GetCustomAttributes(typeof(AiSkillBodyAttribute), inherit: true)
                .Cast<AiSkillBodyAttribute>()
                .FirstOrDefault()?.Body;

        public MethodInfo Method => _methodInfo;

        // EXPERIMENTAL tool binding (IToolBinding): when set, the agent-facing signature, argument conversion and result come
        // from it. One immutable holder swapped as a whole, so a reader never sees a binding with another binding's signature.
        private sealed class Bound
        {
            public Bound(IToolBinding binding, System.Func<ToolSignature?> describe)
            {
                Binding = binding;
                Signature = new System.Lazy<ToolSignature?>(describe, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
            }
            public readonly IToolBinding Binding;
            public readonly System.Lazy<ToolSignature?> Signature;
        }

        private volatile Bound? _bound;

        /// <summary>EXPERIMENTAL. The data layer of this tool; null = ReflectorNet. Usually set through <see cref="IToolManager.ToolBinding"/>.</summary>
        public IToolBinding? Binding
        {
            get => _bound?.Binding;
            set
            {
                if (ReferenceEquals(_bound?.Binding, value)) return;
                _bound = value == null ? null : new Bound(value, () => Describe(value));
                _cachedTokenCount = null;               // the agent-facing catalogue changed
                _cachedSkillMetadataTokenCount = null;
            }
        }

        // A throwing Describe must not break tools/list for every tool (RunListTool fails as a whole) nor stay cached as an
        // exception in the Lazy: log it and keep ReflectorNet's signature for this tool.
        private ToolSignature? Describe(IToolBinding binding)
        {
            try
            {
                return binding.Describe(Name, Method, base.Description);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Tool binding failed to describe tool '{0}'. Keeping the ReflectorNet signature.", Name);
                return null;
            }
        }

        private ToolSignature? BoundSignature() => _bound?.Signature.Value;

        private bool TryBindingConvert(ParameterInfo paramInfo, object? raw, out object? converted)
        {
            converted = null;
            if (_bound?.Binding is not IToolBinding binding || raw is not JsonElement element)
                return false;
            if (!binding.TryConvert(Method, paramInfo, element, out var value))
                return false;
            converted = FixEnumConversion(paramInfo, value);
            return true;
        }

        string? IRunTool.Description => BoundSignature() is { } s ? s.Description : Description;
        JsonNode? IRunTool.InputSchema => BoundSignature() is { } s ? s.InputSchema : InputSchema;
        JsonNode? IRunTool.OutputSchema => BoundSignature() is { } s ? s.OutputSchema : OutputSchema;

        protected string? RequestID { get; set; }

        /// <summary>
        /// Cached lookup dictionary for case-insensitive parameter name matching.
        /// Built once during construction for performance.
        /// </summary>
        private readonly Dictionary<string, string>? _paramNameLookup;

        public RunTool(Reflector reflector, ILogger? logger, string name, MethodInfo methodInfo) : base(reflector, logger, methodInfo)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _paramNameLookup = ParameterNameUtils.BuildParameterNameLookup(methodInfo?.GetParameters());
        }

        public RunTool(Reflector reflector, ILogger? logger, string name, object targetInstance, MethodInfo methodInfo) : base(reflector, logger, targetInstance, methodInfo)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _paramNameLookup = ParameterNameUtils.BuildParameterNameLookup(methodInfo?.GetParameters());
        }

        public RunTool(Reflector reflector, ILogger? logger, string name, Type classType, MethodInfo methodInfo) : base(reflector, logger, classType, methodInfo)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _paramNameLookup = ParameterNameUtils.BuildParameterNameLookup(methodInfo?.GetParameters());
        }

        protected override object? GetParameterValue(Reflector reflector, ParameterInfo paramInfo, object? value)
        {
            if (paramInfo.GetCustomAttribute<RequestIDAttribute>() != null)
            {
                _logger?.LogTrace("Injecting RequestID parameter: {RequestID}", RequestID);
                return RequestID;
            }
            if (TryBindingConvert(paramInfo, value, out var converted))
                return converted;
            return FixEnumConversion(paramInfo, base.GetParameterValue(reflector, paramInfo, value));
        }
        protected override object? GetParameterValue(Reflector reflector, ParameterInfo paramInfo, IReadOnlyDictionary<string, object?>? namedParameters)
        {
            if (paramInfo.GetCustomAttribute<RequestIDAttribute>() != null)
            {
                _logger?.LogTrace("Injecting RequestID parameter: {RequestID}", RequestID);
                return RequestID;
            }

            if (paramInfo.Name != null
                && namedParameters != null
                && namedParameters.TryGetValue(paramInfo.Name, out var raw)
                && TryBindingConvert(paramInfo, raw, out var converted))
                return converted;

            return FixEnumConversion(paramInfo, base.GetParameterValue(reflector, paramInfo, namedParameters));
        }
        protected override object? GetDefaultParameterValue(Reflector reflector, ParameterInfo methodParameter)
        {
            if (methodParameter.GetCustomAttribute<RequestIDAttribute>() != null)
            {
                _logger?.LogTrace("Injecting RequestID parameter: {RequestID}", RequestID);
                return RequestID;
            }
            return FixEnumConversion(methodParameter, base.GetDefaultParameterValue(reflector, methodParameter));
        }

        private object? FixEnumConversion(ParameterInfo paramInfo, object? value)
        {
            if (value != null)
            {
                var paramType = paramInfo.ParameterType;
                var underlyingType = Nullable.GetUnderlyingType(paramType) ?? paramType;

                if (underlyingType.IsEnum && value.GetType() != underlyingType)
                {
                    return Enum.ToObject(underlyingType, value);
                }
            }
            return value;
        }

        protected ResponseCallTool ProcessInvokeResult(string requestId, object? result)
            => ProcessInvokeResult(requestId, result, _bound?.Binding);

        // binding: the one the call started with, so a binding switched while the tool ran does not render a result
        // whose arguments another binding converted.
        private ResponseCallTool ProcessInvokeResult(string requestId, object? result, IToolBinding? binding)
        {
            if (binding?.Result(Method, result) is ResponseCallTool bound)
                return bound.SetRequestID(requestId);

            if (result is ResponseCallTool response)
                return response.SetRequestID(requestId);

            if (result == null)
            {
                return ResponseCallTool
                    .Success()
                    .SetRequestID(requestId);
            }

            return ResponseCallTool
                .SuccessStructured(System.Text.Json.JsonSerializer.SerializeToNode(result, _reflector.JsonSerializerOptions))
                .SetRequestID(requestId);
        }

        /// <summary>
        /// Executes the target static method with the provided arguments.
        /// </summary>
        /// <param name="requestId">The unique identifier for this request.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <param name="parameters">The arguments to pass to the method.</param>
        /// <returns>The result of the method execution, or null if the method is void.</returns>
        public async Task<ResponseCallTool> Run(string requestId, CancellationToken cancellationToken = default, params object?[] parameters)
        {
            var validationResult = ValidateRunParameters(requestId, parameters);
            if (validationResult != null)
                return validationResult;

            RequestID = requestId;
            var binding = _bound?.Binding;
            object? result;
            try
            {
                // Invoke the method (static or instance)
                result = await Invoke(cancellationToken, parameters);
            }
            catch (ArgumentException ex)
            {
                var errorMessage = $"Parameter validation failed for tool '{Title ?? this.Method?.Name}': {ex.Message}";
                _logger?.LogError(ex, errorMessage);
                return ResponseCallTool
                    .Error(errorMessage, ResponseErrorKind.BadRequest)
                    .SetRequestID(requestId);
            }
            catch (TargetParameterCountException ex)
            {
                var errorMessage = $"Parameter count mismatch for tool '{Title ?? this.Method?.Name}'. Expected {this.Method?.GetParameters().Length} parameters, but received {parameters?.Length}";
                _logger?.LogError(ex, errorMessage);
                return ResponseCallTool
                    .Error(errorMessage, ResponseErrorKind.BadRequest)
                    .SetRequestID(requestId);
            }
            catch (Exception ex)
            {
                return ExecutionFailed(requestId, ex);
            }
            return ProcessResultSafely(requestId, result, binding);
        }

        /// <summary>
        /// Executes the target method with named parameters.
        /// Missing parameters will be filled with their default values or the type's default value if no default is defined.
        /// </summary>
        /// <param name="requestId">The unique identifier for this request.</param>
        /// <param name="namedParameters">A dictionary mapping parameter names to their values.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The result of the method execution, or null if the method is void.</returns>
        public async Task<ResponseCallTool> Run(string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default)
        {
            var validationResult = ValidateRunParameters(requestId, namedParameters);
            if (validationResult != null)
                return validationResult;

            RequestID = requestId;
            var binding = _bound?.Binding;
            object? result;
            try
            {
                var finalParameters = ConvertNamedParameters(namedParameters);

                // Invoke the method (static or instance)
                result = await InvokeDict(finalParameters, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                var errorMessage = $"Parameter validation failed for tool '{Title ?? this.Method?.Name}': {ex.Message}";
                _logger?.LogError(ex, errorMessage);
                return ResponseCallTool
                    .Error(errorMessage, ResponseErrorKind.BadRequest)
                    .SetRequestID(requestId);
            }
            catch (JsonException ex)
            {
                var errorMessage = $"JSON parameter parsing failed for tool '{Title ?? this.Method?.Name}': {ex.Message}";
                _logger?.LogError(ex, errorMessage);
                return ResponseCallTool
                    .Error(errorMessage, ResponseErrorKind.BadRequest)
                    .SetRequestID(requestId);
            }
            catch (Exception ex)
            {
                return ExecutionFailed(requestId, ex);
            }
            return ProcessResultSafely(requestId, result, binding);
        }

        /// <summary>
        /// The tool (or its argument binding) threw. The request's content caused it unless the exception is a
        /// <see cref="IsFrameworkFault">framework fault</see>: a fault is <see cref="ResponseErrorKind.Internal"/>
        /// (HTTP 500), a <see cref="ToolUnavailableException"/> is <see cref="ResponseErrorKind.Unavailable"/> (HTTP 503,
        /// <see cref="ResponseCallTool.NotExecuted"/> when it says so), anything else is
        /// <see cref="ResponseCallTool.Unprocessable"/> (HTTP 422). Same message either way.
        /// <paramref name="fault"/> forces 500 (a failure after the tool succeeded).
        /// </summary>
        private ResponseCallTool ExecutionFailed(string requestId, Exception ex, bool fault = false)
        {
            var errorMessage = $"Tool execution failed for '{Title ?? this.Method?.Name}': {(ex.InnerException ?? ex).Message}";
            _logger?.LogError(ex, $"{errorMessage}\n{ex.StackTrace}");
            var response = fault || IsFrameworkFault(ex)
                ? ResponseCallTool.Error(errorMessage, ResponseErrorKind.Internal)
                : Unwrap(ex) is ToolUnavailableException unavailable
                    // The plain kind carries no status: Unavailable + 503 is the not-executed marker a server re-sends.
                    ? unavailable.NotExecuted
                        ? ResponseCallTool.NotExecuted(errorMessage)
                        : ResponseCallTool.Error(errorMessage, ResponseErrorKind.Unavailable)
                    : ResponseCallTool.Unprocessable(errorMessage);
            return response.SetRequestID(requestId);
        }

        /// <summary>
        /// Turns the tool's return value into the response. The tool has already succeeded here, so a failure
        /// (a result that cannot be serialized, a binding that cannot render it) is ours: HTTP 500.
        /// </summary>
        private ResponseCallTool ProcessResultSafely(string requestId, object? result, IToolBinding? binding)
        {
            try
            {
                return ProcessInvokeResult(requestId, result, binding);
            }
            catch (Exception ex)
            {
                return ExecutionFailed(requestId, ex, fault: true);
            }
        }

        /// <summary>
        /// True when an exception a tool threw is a bug or fault in the plugin rather than something the request
        /// caused: <see cref="NullReferenceException"/>, <see cref="IndexOutOfRangeException"/>,
        /// <see cref="InvalidCastException"/>, <see cref="NotImplementedException"/>, <see cref="ObjectDisposedException"/>,
        /// <see cref="OutOfMemoryException"/>; a plugin built against other assemblies or failing to load
        /// (<see cref="TypeLoadException"/>, <see cref="MissingMemberException"/>, <see cref="TypeInitializationException"/>,
        /// <see cref="FileLoadException"/>, <see cref="BadImageFormatException"/>, <see cref="InvalidProgramException"/>);
        /// and <see cref="TimeoutException"/> / <see cref="OperationCanceledException"/> (the call ran out of time or was
        /// cancelled, it did not fail on its content). Subclasses count too. Reflection and single-exception aggregate wrappers are looked through.
        /// Every other exception, a plain <see cref="Exception"/> included, is the tool rejecting what was sent.
        /// </summary>
        internal static bool IsFrameworkFault(Exception exception)
            => Unwrap(exception) is NullReferenceException
                or IndexOutOfRangeException
                or InvalidCastException
                or NotImplementedException
                or ObjectDisposedException
                or OutOfMemoryException
                or TypeLoadException
                or MissingMemberException
                or TypeInitializationException
                or FileLoadException
                or BadImageFormatException
                or InvalidProgramException
                or TimeoutException
                or OperationCanceledException;

        /// <summary>The exception the tool threw: reflection and single-exception aggregate wrappers are looked through.</summary>
        static Exception Unwrap(Exception exception)
        {
            var ex = exception;
            while (true)
            {
                if (ex is TargetInvocationException { InnerException: { } invoked })
                    ex = invoked;
                else if (ex is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
                    ex = aggregate.InnerExceptions[0];
                else
                    return ex;
            }
        }

        /// <summary>
        /// Validates common parameters for tool execution.
        /// </summary>
        /// <param name="requestId">The request identifier to validate.</param>
        /// <param name="parameters">Additional parameters for context.</param>
        /// <returns>An error response if validation fails, null if validation passes.</returns>
        private ResponseCallTool? ValidateRunParameters(string requestId, object? parameters = null)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                var errorMessage = $"Request ID cannot be null or empty for tool '{Title ?? this.Method?.Name}'";
                _logger?.LogError(errorMessage);
                return ResponseCallTool
                    .Error(errorMessage, ResponseErrorKind.BadRequest)
                    .SetRequestID(requestId);
            }

            if (this.Method == null)
            {
                var errorMessage = $"Method information is not available for tool '{Title}'";
                _logger?.LogError(errorMessage);
                return ResponseCallTool
                    .Error(errorMessage, ResponseErrorKind.Internal)
                    .SetRequestID(requestId);
            }

            // Validate method is accessible
            if (!this.Method.IsPublic && !this.Method.IsFamily)
            {
                var errorMessage = $"Method '{this.Method.Name}' in tool '{Title}' is not accessible (must be public or protected)";
                _logger?.LogError(errorMessage);
                return ResponseCallTool
                    .Error(errorMessage, ResponseErrorKind.Internal)
                    .SetRequestID(requestId);
            }

            return null; // Validation passed
        }

        /// <summary>
        /// Converts named parameters from JsonElement dictionary to object dictionary with improved error handling.
        /// Also normalizes parameter names using case-insensitive matching when there's no conflict.
        /// </summary>
        /// <param name="namedParameters">The named parameters to convert.</param>
        /// <returns>A dictionary with object values.</returns>
        private Dictionary<string, object?>? ConvertNamedParameters(IReadOnlyDictionary<string, JsonElement>? namedParameters)
        {
            if (namedParameters == null)
                return null;

            try
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);

                foreach (var kvp in namedParameters)
                {
                    var normalizedKey = ParameterNameUtils.NormalizeParameterName(kvp.Key, _paramNameLookup);

                    // Check for duplicate keys after normalization (e.g., LLM provided both "value" and "VALUE")
                    if (result.ContainsKey(normalizedKey))
                    {
                        throw new ArgumentException(
                            $"Duplicate parameter detected after case-insensitive normalization: '{kvp.Key}' normalizes to '{normalizedKey}' which already exists. " +
                            $"Please provide each parameter only once.");
                    }

                    result[normalizedKey] = kvp.Value;
                }

                return result;
            }
            catch (ArgumentException)
            {
                throw; // Re-throw ArgumentException as-is
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to convert named parameters: {ex.Message}", ex);
            }
        }
    }
}
