using Json.Schema;

namespace Gauge.Tools.ToolValidation;

internal static class JsonSchemaLoader
{
    public static JsonSchema Parse(string schemaJson)
    {
        try
        {
            return JsonSchema.FromText(schemaJson);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Invalid JSON Schema text.", ex);
        }
    }
}