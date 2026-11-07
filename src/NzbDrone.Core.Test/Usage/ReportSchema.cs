using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace NzbDrone.Core.Test.Usage
{
    // Checks a report against usage/schema/v1/report.schema.json, the schema usage.nodearr.com
    // refuses anything else by. It knows the parts of JSON Schema that file uses, and fails on any
    // other, so a schema change that needs more is noticed here.
    public static class ReportSchema
    {
        private static readonly HashSet<string> Known = new()
        {
            "$schema", "$id", "$defs", "$ref", "title", "description", "type", "const", "enum", "properties",
            "required", "additionalProperties", "propertyNames", "pattern", "maxLength", "items", "maxItems"
        };

        public static JsonElement Load()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Usage", "report.schema.json");

            return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        }

        public static List<string> Errors(string json)
        {
            var schema = Load();
            var errors = new List<string>();

            Check(schema, schema, JsonDocument.Parse(json).RootElement, "$", errors);

            return errors;
        }

        private static void Check(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors)
        {
            foreach (var keyword in schema.EnumerateObject())
            {
                if (!Known.Contains(keyword.Name))
                {
                    errors.Add($"{path}: the schema uses {keyword.Name}, which this check doesn't know");
                }
            }

            if (schema.TryGetProperty("$ref", out var reference))
            {
                var name = reference.GetString().Replace("#/$defs/", string.Empty);

                Check(root, root.GetProperty("$defs").GetProperty(name), value, path, errors);
                return;
            }

            if (schema.TryGetProperty("const", out var constant) && value.GetRawText() != constant.GetRawText())
            {
                errors.Add($"{path}: should be {constant.GetRawText()}");
            }

            if (schema.TryGetProperty("enum", out var allowed) && allowed.EnumerateArray().All(a => a.GetRawText() != value.GetRawText()))
            {
                errors.Add($"{path}: {value.GetRawText()} isn't one of the values allowed");
            }

            if (schema.TryGetProperty("type", out var type))
            {
                var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()).ToList() : new List<string> { type.GetString() };

                if (!types.Contains(TypeOf(value)))
                {
                    errors.Add($"{path}: is {TypeOf(value)}, not {string.Join(" or ", types)}");
                    return;
                }
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    var text = value.GetString();

                    if (schema.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(text, pattern.GetString()))
                    {
                        errors.Add($"{path}: \"{text}\" doesn't match {pattern.GetString()}");
                    }

                    if (schema.TryGetProperty("maxLength", out var maxLength) && text.Length > maxLength.GetInt32())
                    {
                        errors.Add($"{path}: is longer than {maxLength.GetInt32()}");
                    }

                    break;

                case JsonValueKind.Array:
                    if (schema.TryGetProperty("maxItems", out var maxItems) && value.GetArrayLength() > maxItems.GetInt32())
                    {
                        errors.Add($"{path}: has more than {maxItems.GetInt32()} items");
                    }

                    if (schema.TryGetProperty("items", out var items))
                    {
                        var i = 0;

                        foreach (var item in value.EnumerateArray())
                        {
                            Check(root, items, item, $"{path}[{i++}]", errors);
                        }
                    }

                    break;

                case JsonValueKind.Object:
                    CheckObject(root, schema, value, path, errors);
                    break;
            }
        }

        private static void CheckObject(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors)
        {
            var hasProperties = schema.TryGetProperty("properties", out var properties);

            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var name in required.EnumerateArray().Select(r => r.GetString()).Where(n => !value.TryGetProperty(n, out _)))
                {
                    errors.Add($"{path}: {name} is missing");
                }
            }

            foreach (var property in value.EnumerateObject())
            {
                var at = $"{path}.{property.Name}";

                if (schema.TryGetProperty("propertyNames", out var names))
                {
                    Check(root, names, JsonDocument.Parse(JsonSerializer.Serialize(property.Name)).RootElement, at, errors);
                }

                if (hasProperties && properties.TryGetProperty(property.Name, out var propertySchema))
                {
                    Check(root, propertySchema, property.Value, at, errors);
                }
                else if (schema.TryGetProperty("additionalProperties", out var additional))
                {
                    if (additional.ValueKind == JsonValueKind.False)
                    {
                        errors.Add($"{at}: isn't a field the schema allows");
                    }
                    else if (additional.ValueKind == JsonValueKind.Object)
                    {
                        Check(root, additional, property.Value, at, errors);
                    }
                }
            }
        }

        private static string TypeOf(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.Object => "object",
                JsonValueKind.Array => "array",
                JsonValueKind.String => "string",
                JsonValueKind.Number => value.TryGetInt64(out _) ? "integer" : "number",
                JsonValueKind.True or JsonValueKind.False => "boolean",
                _ => "null"
            };
        }
    }
}
