using System.Text;

namespace ResortWise.LoanBalance.UnitTests.Scripts;

/// <summary>Minimal T-SQL text helpers for the script checks. Not a parser.</summary>
public static class SqlText
{
    /// <summary>Removes <c>--</c> and <c>/* */</c> comments, keeping string literals.</summary>
    public static string StripComments(string sql) => Strip(sql, removeStrings: false);

    /// <summary>Removes comments and replaces string literal contents, so keywords inside strings are ignored.</summary>
    public static string StripCommentsAndStrings(string sql) => Strip(sql, removeStrings: true);

    private static string Strip(string sql, bool removeStrings)
    {
        var result = new StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }
            }
            else if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 2;
                result.Append(' ');
            }
            else if (sql[i] == '\'')
            {
                var start = i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        i += 2;
                    }
                    else if (sql[i] == '\'')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        i++;
                    }
                }

                result.Append(removeStrings ? "''" : sql[start..i]);
            }
            else
            {
                result.Append(sql[i++]);
            }
        }

        return result.ToString();
    }
}
