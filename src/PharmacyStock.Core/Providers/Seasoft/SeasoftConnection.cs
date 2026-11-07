using System.Data.Common;
using System.Data.Odbc;
using System.Data.OleDb;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace PharmacyStock.Providers.Seasoft;

/// <summary>Database engines a pharmacy system of this kind realistically uses.</summary>
public enum SeasoftEngine
{
    SqlServer = 0,
    MySql = 1,
    Firebird = 2,
    Access = 3,
    Odbc = 4,
}

/// <summary>
/// How to reach the SeaSoft database. Saved in our own settings, never in SeaSoft.
///
/// The password is stored encrypted with the Windows user's own key (see
/// <see cref="Secrets"/>), so it is not sitting in plain text on a counter PC.
/// </summary>
public sealed record SeasoftConnection
{
    public SeasoftEngine Engine { get; init; } = SeasoftEngine.SqlServer;

    /// <summary>Server PC name or IP. For SQL Server may include an instance, e.g. SERVER\SQLEXPRESS.</summary>
    public string Server { get; init; } = "";

    /// <summary>0 means the engine's normal port.</summary>
    public int Port { get; init; }

    public string Database { get; init; } = "";

    /// <summary>Database file, for Access (.mdb/.accdb) and Firebird (.fdb/.gdb).</summary>
    public string FilePath { get; init; } = "";

    public string User { get; init; } = "";
    public string Password { get; init; } = "";

    /// <summary>SQL Server only: sign in as the logged-in Windows user instead of a username.</summary>
    public bool WindowsAuth { get; init; } = true;

    /// <summary>For the ODBC option: either a DSN name or a full ODBC connection string.</summary>
    public string Odbc { get; init; } = "";

    public bool IsConfigured => Engine switch
    {
        SeasoftEngine.Access or SeasoftEngine.Firebird => FilePath.Trim().Length > 0,
        SeasoftEngine.Odbc => Odbc.Trim().Length > 0,
        _ => Server.Trim().Length > 0 && Database.Trim().Length > 0,
    };

    public string Describe() => Engine switch
    {
        SeasoftEngine.Access => $"Access file {FilePath}",
        SeasoftEngine.Firebird => $"Firebird {Server}:{FilePath}",
        SeasoftEngine.Odbc => $"ODBC {Odbc}",
        SeasoftEngine.MySql => $"MySQL {Server}/{Database}",
        _ => $"SQL Server {Server}/{Database}",
    };

    /// <summary>
    /// Builds the connection string. Timeouts are deliberately short: if the server PC is
    /// off, the program must say so quickly rather than freeze the counter screen.
    /// </summary>
    public string BuildConnectionString()
    {
        switch (Engine)
        {
            case SeasoftEngine.SqlServer:
            {
                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = Port > 0 ? $"{Server},{Port}" : Server,
                    InitialCatalog = Database,
                    ConnectTimeout = 10,
                    TrustServerCertificate = true,     // local servers rarely have a real certificate
                    Encrypt = false,
                    Pooling = true,
                };

                if (WindowsAuth)
                {
                    builder.IntegratedSecurity = true;
                }
                else
                {
                    builder.UserID = User;
                    builder.Password = Password;
                }

                return builder.ConnectionString;
            }

            case SeasoftEngine.MySql:
            {
                var builder = new MySqlConnectionStringBuilder
                {
                    Server = Server,
                    Port = (uint)(Port > 0 ? Port : 3306),
                    Database = Database,
                    UserID = User,
                    Password = Password,
                    ConnectionTimeout = 10,
                };
                return builder.ConnectionString;
            }

            case SeasoftEngine.Firebird:
            {
                var builder = new FbConnectionStringBuilder
                {
                    DataSource = Server.Trim().Length > 0 ? Server : "localhost",
                    Port = Port > 0 ? Port : 3050,
                    Database = FilePath,
                    UserID = User.Trim().Length > 0 ? User : "SYSDBA",
                    Password = Password,
                    Charset = "NONE",
                    ConnectionTimeout = 10,
                };
                return builder.ConnectionString;
            }

            case SeasoftEngine.Access:
            {
                // Mode=Read asks the provider to open the file without taking a write lock,
                // so SeaSoft can carry on using it normally.
                var provider = FilePath.EndsWith(".accdb", StringComparison.OrdinalIgnoreCase)
                    ? "Microsoft.ACE.OLEDB.12.0"
                    : "Microsoft.Jet.OLEDB.4.0";

                var parts = new List<string>
                {
                    $"Provider={provider}",
                    $"Data Source={FilePath}",
                    "Mode=Read",
                };
                if (Password.Trim().Length > 0) parts.Add($"Jet OLEDB:Database Password={Password}");
                return string.Join(";", parts);
            }

            case SeasoftEngine.Odbc:
            {
                var text = Odbc.Trim();
                return text.Contains('=') ? text : $"DSN={text}";
            }

            default:
                throw new NotSupportedException("Unknown database type.");
        }
    }

    /// <summary>Opens a connection. The caller disposes it.</summary>
    public DbConnection CreateConnection()
    {
        var text = BuildConnectionString();
        return Engine switch
        {
            SeasoftEngine.SqlServer => new SqlConnection(text),
            SeasoftEngine.MySql => new MySqlConnection(text),
            SeasoftEngine.Firebird => new FbConnection(text),
            SeasoftEngine.Access => CreateOleDb(text),
            SeasoftEngine.Odbc => CreateOdbc(text),
            _ => throw new NotSupportedException("Unknown database type."),
        };
    }

    private static DbConnection CreateOleDb(string text)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Access databases can only be read on Windows.");
        return new OleDbConnection(text);
    }

    private static DbConnection CreateOdbc(string text)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("ODBC is only available on Windows here.");
        return new OdbcConnection(text);
    }

    /// <summary>Wraps a table or column name the way this engine expects.</summary>
    public string Quote(string identifier)
    {
        var parts = identifier.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var quoted = parts.Select(part => Engine switch
        {
            SeasoftEngine.MySql => "`" + part.Replace("`", "``") + "`",
            SeasoftEngine.Firebird => "\"" + part.Replace("\"", "\"\"") + "\"",
            _ => "[" + part.Replace("]", "]]") + "]",
        });
        return string.Join(".", quoted);
    }

    /// <summary>A SELECT that returns at most <paramref name="rows"/> rows, in this engine's dialect.</summary>
    public string TopRows(string table, int rows) => Engine switch
    {
        SeasoftEngine.MySql => $"SELECT * FROM {Quote(table)} LIMIT {rows}",
        SeasoftEngine.Firebird => $"SELECT FIRST {rows} * FROM {Quote(table)}",
        _ => $"SELECT TOP {rows} * FROM {Quote(table)}",
    };
}
