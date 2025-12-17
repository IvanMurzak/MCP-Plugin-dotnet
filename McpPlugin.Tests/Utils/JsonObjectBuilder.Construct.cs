using System.Collections.Generic;
using System.Text.Json.Nodes;
using com.IvanMurzak.McpPlugin.Tests.Data.Other;
using com.IvanMurzak.ReflectorNet.Utils;

namespace com.IvanMurzak.McpPlugin.Common.Tests.Utils
{
    internal static class JsonObjectBuilderConstructExtensions
    {
        public static JsonObjectBuilder AddCompanyDefine(this JsonObjectBuilder builder)
        {
            return builder

                // Address definition
                .AddDefinition(
                    name: "com.IvanMurzak.McpPlugin.Tests.Data.Other.Address",
                    definition: new JsonObjectBuilder()
                        .SetTypeObject()
                        .AddSimpleProperty(nameof(Address.Street), JsonSchema.String, required: false)
                        .AddSimpleProperty(nameof(Address.City), JsonSchema.String, required: false)
                        .AddSimpleProperty(nameof(Address.Zip), JsonSchema.String, required: false)
                        .AddSimpleProperty(nameof(Address.Country), JsonSchema.String, required: false)
                        .BuildJsonObject())

                // List<Person> array definition
                .AddArrayDefinitionRef(
                    name: "System.Collections.Generic.List<com.IvanMurzak.McpPlugin.Tests.Data.Other.Person>",
                    itemType: "com.IvanMurzak.McpPlugin.Tests.Data.Other.Person")

                // Person definition
                .AddDefinition(
                    name: "com.IvanMurzak.McpPlugin.Tests.Data.Other.Person",
                    definition: new JsonObjectBuilder()
                        .SetTypeObject()
                        .AddSimpleProperty(nameof(Person.FirstName), JsonSchema.String, required: false)
                        .AddSimpleProperty(nameof(Person.LastName), JsonSchema.String, required: false)
                        .AddSimpleProperty(nameof(Person.Age), JsonSchema.Integer, required: true)
                        .AddRefProperty<Address>(nameof(Person.Address), required: false)
                        .AddRefProperty<List<string>>(nameof(Person.Tags), required: false)
                        .AddRefProperty<Dictionary<string, int>>(nameof(Person.Scores), required: false)
                        .AddRefProperty<int[]>(nameof(Person.Numbers), required: false)
                        .AddRefProperty<string[][]>(nameof(Person.JaggedAliases), required: false)
                        .AddRefProperty<int[,]>(nameof(Person.Matrix2x2), required: false)
                        .BuildJsonObject())

                // List<string> definition
                .AddArrayDefinition(
                    name: "System.Collections.Generic.List<System.String>",
                    itemType: JsonSchema.String)

                // Dictionary<string, int> definition
                .AddDefinition(
                    name: "System.Collections.Generic.Dictionary<System.String,System.Int32>",
                    definition: new JsonObject
                    {
                        [JsonSchema.Type] = JsonSchema.Object,
                        [JsonSchema.AdditionalProperties] = new JsonObject
                        {
                            [JsonSchema.Type] = JsonSchema.Integer
                        }
                    })

                // int[] array definition
                .AddArrayDefinition(
                    name: "System.Int32[]",
                    itemType: JsonSchema.Integer)

                // string[][] jagged array definition
                .AddArrayDefinitionRef(
                    name: "System.String[][]",
                    itemType: "System.String[]")

                // string[] array definition
                .AddArrayDefinition(
                    name: "System.String[]",
                    itemType: JsonSchema.String)

                // int[,] array definition
                .AddDefinition(
                    name: "System.Int32[,]",
                    definition: new JsonObject
                    {
                        [JsonSchema.Type] = JsonSchema.Array,
                        [JsonSchema.Items] = new JsonObject
                        {
                            [JsonSchema.Type] = JsonSchema.Array,
                            [JsonSchema.Items] = new JsonObject
                            {
                                [JsonSchema.Type] = JsonSchema.Integer
                            }
                        }
                    })

                // Dictionary<string, List<Person>> definition
                .AddDefinition(
                    name: "System.Collections.Generic.Dictionary<System.String,System.Collections.Generic.List<com.IvanMurzak.McpPlugin.Tests.Data.Other.Person>>",
                    definition: new JsonObject
                    {
                        [JsonSchema.Type] = JsonSchema.Object,
                        [JsonSchema.AdditionalProperties] = new JsonObject
                        {
                            [JsonSchema.Type] = JsonSchema.Array,
                            [JsonSchema.Items] = new JsonObject
                            {
                                [JsonSchema.Ref] = JsonSchema.RefValue + TypeUtils.GetSchemaTypeId<Person>()
                            }
                        }
                    })

                // Dictionary<string, Dictionary<string, Person>> definition
                .AddDefinition(
                    name: "System.Collections.Generic.Dictionary<System.String,System.Collections.Generic.Dictionary<System.String,com.IvanMurzak.McpPlugin.Tests.Data.Other.Person>>",
                    definition: new JsonObject
                    {
                        [JsonSchema.Type] = JsonSchema.Object,
                        [JsonSchema.AdditionalProperties] = new JsonObject
                        {
                            [JsonSchema.Type] = JsonSchema.Object,
                            [JsonSchema.AdditionalProperties] = new JsonObjectBuilder()
                                .SetTypeObject()
                                .AddSimpleProperty(nameof(Person.FirstName), JsonSchema.String, required: false)
                                .AddSimpleProperty(nameof(Person.LastName), JsonSchema.String, required: false)
                                .AddSimpleProperty(nameof(Person.Age), JsonSchema.Integer, required: true)
                                .AddRefProperty<Address>(nameof(Person.Address), required: false)
                                .AddRefProperty<List<string>>(nameof(Person.Tags), required: false)
                                .AddRefProperty<Dictionary<string, int>>(nameof(Person.Scores), required: false)
                                .AddRefProperty<int[]>(nameof(Person.Numbers), required: false)
                                .AddRefProperty<string[][]>(nameof(Person.JaggedAliases), required: false)
                                .AddRefProperty<int[,]>(nameof(Person.Matrix2x2), required: false)
                                .BuildJsonObject()
                        }
                    })

                // Dictionary<string, Person> definition
                .AddDefinition(
                    name: "System.Collections.Generic.Dictionary<System.String,com.IvanMurzak.McpPlugin.Tests.Data.Other.Person>",
                    definition: new JsonObject
                    {
                        [JsonSchema.Type] = JsonSchema.Object,
                        [JsonSchema.AdditionalProperties] = new JsonObjectBuilder()
                            .SetTypeObject()
                            .AddSimpleProperty(nameof(Person.FirstName), JsonSchema.String, required: false)
                            .AddSimpleProperty(nameof(Person.LastName), JsonSchema.String, required: false)
                            .AddSimpleProperty(nameof(Person.Age), JsonSchema.Integer, required: true)
                            .AddRefProperty<Address>(nameof(Person.Address), required: false)
                            .AddRefProperty<List<string>>(nameof(Person.Tags), required: false)
                            .AddRefProperty<Dictionary<string, int>>(nameof(Person.Scores), required: false)
                            .AddRefProperty<int[]>(nameof(Person.Numbers), required: false)
                            .AddRefProperty<string[][]>(nameof(Person.JaggedAliases), required: false)
                            .AddRefProperty<int[,]>(nameof(Person.Matrix2x2), required: false)
                            .BuildJsonObject()
                    })

                // Company definition
                .AddDefinition(
                    TypeUtils.GetSchemaTypeId<Company>(),
                    new JsonObjectBuilder()
                        .SetTypeObject()
                        .AddSimpleProperty(nameof(Company.Name), JsonSchema.String, required: false)
                        .AddRefProperty<Address>(nameof(Company.Headquarters), required: false)
                        .AddRefProperty<List<Person>>(nameof(Company.Employees), required: false)
                        .AddRefProperty<Dictionary<string, List<Person>>>(nameof(Company.Teams), required: false)
                        .AddRefProperty<Dictionary<string, Dictionary<string, Person>>>(nameof(Company.Directory), required: false)
                        .BuildJsonObject());
        }
    }
}