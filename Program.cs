using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using com.IvanMurzak.ReflectorNet;

var reflector = new Reflector();

var inputMethod = typeof(TestClass).GetMethod("ProcessMember");
var outputMethod = typeof(TestClass).GetMethod("GetMember");

Console.WriteLine("=== Input Schema (method takes SerializedMember) ===");
var inputSchema = reflector.JsonSchema.GetArgumentsSchema(reflector, inputMethod, justRef: false, defines: null);
Console.WriteLine(inputSchema?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine("\n=== Output Schema (method returns SerializedMember) ===");
var outputSchema = reflector.JsonSchema.GetReturnSchema(reflector, outputMethod, justRef: false, defines: null);
Console.WriteLine(outputSchema?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
