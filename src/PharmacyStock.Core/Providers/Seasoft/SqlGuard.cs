using System.Text;

namespace PharmacyStock.Providers.Seasoft;

/// <summary>
/// Last line of defence for the promise that this program never writes to SeaSoft.
///
/// Every statement sent to the pharmacy database passes through here first. Anything
/// that is not a plain SELECT is refused before it reaches the server - including a
/// query typed by hand in the Advanced box. A read-only database login is still the
/// right thing to ask the vendor for; this makes sure a mistake on our side cannot
/// damage anything even without one.
/// </summary>
public static class SqlGuard
{
    private static readonly string[] Forbidden =
    {
        "insert", "update", "delete", "drop", "alter", "create", "truncate", "merge",
        "exec", "execute", "grant", "revoke", "backup", "restore", "shutdown",
        "sp_", "xp_", "into",
    };

    /// <summary>Throws if the statement is anything other than a single read.</summary>
    public static void EnsureReadOnly(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException("The query is empty.");

        var bare = StripCommentsAndText(sql).Trim();

        if (bare.Length == 0)
            throw new InvalidOperationException("The query has no statement in it.");

        var lower = bare.ToLowerInvariant();

        if (!lower.StartsWith("select", StringComparison.Ordinal) &&
            !lower.StartsWith("with", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Only SELECT queries are allowed. This program is not permitted to change the pharmacy database.");
        }

        // Two statements separated by a semicolon would let a write slip in behind a read.
        if (bare.TrimEnd(';').Contains(';'))
            throw new InvalidOperationException("Only one SELECT statement is allowed.");

        foreach (var word in Forbidden)
        {
            if (!ContainsWord(lower, word)) continue;
            throw new InvalidOperationException(
                $"The query contains \"{word.TrimEnd('_')}\", which could change data. Only plain SELECT queries are allowed.");
        }
    }

    /// <summary>True when the statement is a safe read, without throwing.</summary>
    public static bool IsReadOnly(string sql)
    {
        try { EnsureReadOnly(sql); return true; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>
    /// Removes quoted text and comments, so a medicine called "Update Syrup" cannot trip
    /// the checks and a write cannot hide inside a comment.
    /// </summary>
    private static string StripCommentsAndText(string sql)
    {
        var output = new StringBuilder(sql.Length);

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];

            // -- line comment
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                output.Append(' ');
                continue;
            }

            // /* block comment */
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                i++;
                output.Append(' ');
                continue;
            }

            // 'string literal'
            if (c == '\'')
            {
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'') { i += 2; continue; }
                    if (sql[i] == '\'') break;
                    i++;
                }
                output.Append(" '' ");
                continue;
            }

            // [quoted identifier] and "quoted identifier" and `quoted identifier`
            if (c is '[' or '"' or '`')
            {
                var close = c switch { '[' => ']', '"' => '"', _ => '`' };
                i++;
                while (i < sql.Length && sql[i] != close) i++;
                output.Append(" id ");
                continue;
            }

            output.Append(c);
        }

        return output.ToString();
    }

    /// <summary>Whole-word match, so "deleted_at" is not mistaken for DELETE.</summary>
    private static bool ContainsWord(string text, string word)
    {
        var from = 0;
        while (true)
        {
            var at = text.IndexOf(word, from, StringComparison.Ordinal);
            if (at < 0) return false;

            var beforeOk = at == 0 || !IsWordChar(text[at - 1]);
            var end = at + word.Length;

            // "sp_" and "xp_" are prefixes, so anything may follow them.
            var afterOk = word.EndsWith('_') || end >= text.Length || !IsWordChar(text[end]);

            if (beforeOk && afterOk) return true;
            from = at + 1;
        }
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
