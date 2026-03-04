using System.Text.Json;

namespace Gauge.Tools.ToolValidation;

internal static class JsonPointer
{
    // RFC6901: "/a/b/0"
    public static bool TryResolve(JsonElement root, string pointer, out JsonElement value)
    {
        value = root;

        if (string.IsNullOrWhiteSpace(pointer) || pointer == "/")
            return true;

        if (!pointer.StartsWith('/'))
            return false;

        var parts = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)
                           .Select(Unescape);

        var current = root;

        foreach (var part in parts)
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(part, out current))
                {
                    value = default;
                    return false;
                }
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                if (!int.TryParse(part, out var idx))
                {
                    value = default;
                    return false;
                }

                if (idx < 0 || idx >= current.GetArrayLength())
                {
                    value = default;
                    return false;
                }

                current = current[idx];
            }
            else
            {
                value = default;
                return false;
            }
        }

        value = current;
        return true;
    }

    private static string Unescape(string token)
        => token.Replace("~1", "/").Replace("~0", "~");
}