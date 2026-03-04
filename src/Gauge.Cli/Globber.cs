using System.Text.RegularExpressions;

namespace Gauge.Cli;

internal static class Globber
{
    public static IEnumerable<string> Enumerate(string rootDir, string globPattern)
    {
        // Normaliza separadores
        rootDir = Path.GetFullPath(rootDir);

        // Enumerar tudo e filtrar por glob (simples e confiável)
        // Para performance, isso é ok para suites pequenas/médias (centenas/milhares de arquivos).
        var files = Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories);

        var regex = GlobToRegex(globPattern);

        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(rootDir, file)
                .Replace('\\', '/');

            if (regex.IsMatch(rel))
                yield return file;
        }
    }

    private static Regex GlobToRegex(string pattern)
    {
        // Glob com suporte a:
        // ** => qualquer diretório (incluindo vazio)
        // *  => qualquer sequência sem '/'
        // ?  => um char sem '/'
        // Normaliza para separador '/'
        pattern = (pattern ?? "**/*.json").Replace('\\', '/').Trim();

        var re = new System.Text.StringBuilder();
        re.Append("^");

        for (int i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];

            if (c == '*')
            {
                var isDoubleStar = i + 1 < pattern.Length && pattern[i + 1] == '*';
                if (isDoubleStar)
                {
                    // Se for "**/" => zero ou mais diretórios (inclui vazio)
                    var hasSlashAfter = i + 2 < pattern.Length && pattern[i + 2] == '/';
                    if (hasSlashAfter)
                    {
                        re.Append("(?:.*/)?"); // aceita "": raiz, ou "a/", ou "a/b/"
                        i += 2; // consome os dois '*' e a '/'
                    }
                    else
                    {
                        re.Append(".*"); // qualquer coisa, incluindo '/'
                        i++; // consome o segundo '*'
                    }

                    continue;
                }

                // * => qualquer coisa exceto '/'
                re.Append("[^/]*");
                continue;
            }

            if (c == '?')
            {
                re.Append("[^/]");
                continue;
            }

            // escape regex chars
            if ("+()^$.{}[]|\\".Contains(c))
                re.Append('\\');

            re.Append(c);
        }

        re.Append("$");
        return new Regex(re.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}