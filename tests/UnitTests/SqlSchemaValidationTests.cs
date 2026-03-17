//using System.Text.RegularExpressions;
//using System.Text.Json;
//using Xunit;

//namespace UnitTests;

//public sealed class SqlSchemaValidationTests
//{
//    private static readonly HashSet<string> AllowedMissingTables = ["webhook_events"];

//    [Fact]
//    //public void SqlIdentifiers_ShouldMatch_DbSchemaDump()
//    //{
//    //    var repoRoot = FindRepoRoot();
//    //    var schemaPath = Path.Combine(repoRoot, "docs", "db-schema-dump.json");
//    //    var schemaRows = JsonSerializer.Deserialize<List<SchemaRow>>(File.ReadAllText(schemaPath)) ?? [];

//    //    var schema = schemaRows
//    //        .GroupBy(r => r.table)
//    //        .ToDictionary(g => g.Key, g => g.Select(x => x.column).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

//    //    var failures = new List<string>();
//    //    var repoFiles = Directory.GetFiles(Path.Combine(repoRoot, "src", "Infrastructure", "Repositories"), "*.cs", SearchOption.AllDirectories);

//    //    foreach (var file in repoFiles)
//    //    {
//    //        var content = File.ReadAllText(file);
//    //        foreach (Match sqlMatch in Regex.Matches(content, "(?:const|var)\\s+string\\s+\\w+\\s*=\\s*@?\\$?\"([\\s\\S]*?)\";", RegexOptions.Multiline))
//    //        {
//    //            var sql = sqlMatch.Groups[1].Value;
//    //            ValidateSql(file, sql, schema, failures);
//    //        }
//    //    }

//    //    Assert.True(failures.Count == 0, "Falhas de schema SQL:\n" + string.Join("\n", failures));
//    //}

//    private static void ValidateSql(string file, string sql, Dictionary<string, HashSet<string>> schema, List<string> failures)
//    {
//        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
//        foreach (Match m in Regex.Matches(sql, "\\b(?:from|join|update|into)\\s+\"?([A-Za-z_][A-Za-z0-9_]*)\"?(?:\\s+([A-Za-z_][A-Za-z0-9_]*))?", RegexOptions.IgnoreCase))
//        {
//            var table = m.Groups[1].Value;
//            var alias = m.Groups[2].Value;

//            if (SqlKeywords.Contains(table))
//            {
//                continue;
//            }

//            if (!schema.ContainsKey(table) && !AllowedMissingTables.Contains(table))
//            {
//                failures.Add($"{Path.GetFileName(file)}: tabela inexistente '{table}' em SQL => {OneLine(sql)}");
//            }

//            if (!string.IsNullOrWhiteSpace(alias) && !SqlKeywords.Contains(alias))
//            {
//                aliases[alias] = table;
//            }
//        }

//        foreach (Match m in Regex.Matches(sql, "\\b([A-Za-z_][A-Za-z0-9_]*)\\.\"?([A-Za-z_][A-Za-z0-9_]*)\"?"))
//        {
//            var alias = m.Groups[1].Value;
//            var column = m.Groups[2].Value;

//            if (!aliases.TryGetValue(alias, out var table))
//            {
//                continue;
//            }

//            if (AllowedMissingTables.Contains(table))
//            {
//                continue;
//            }

//            if (!schema.TryGetValue(table, out var columns))
//            {
//                continue;
//            }

//            if (!columns.Contains(column))
//            {
//                failures.Add($"{Path.GetFileName(file)}: coluna inexistente '{table}.{column}' em SQL => {OneLine(sql)}");
//            }
//        }
//    }

//    private static string OneLine(string sql) => Regex.Replace(sql, "\\s+", " ").Trim();

//    private static string FindRepoRoot()
//    {
//        var dir = new DirectoryInfo(AppContext.BaseDirectory);
//        while (dir is not null)
//        {
//            if (File.Exists(Path.Combine(dir.FullName, "Jobeasy.sln")))
//            {
//                return dir.FullName;
//            }

//            dir = dir.Parent;
//        }

//        throw new DirectoryNotFoundException("Não foi possível localizar a raiz do repositório.");
//    }

//    private static readonly HashSet<string> SqlKeywords =
//    [
//        "where", "on", "set", "values", "order", "group", "limit"
//    ];

//    private sealed record SchemaRow(string table, int pos, string column, string type, string nullable);
//}
