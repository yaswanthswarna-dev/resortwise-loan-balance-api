using System.Reflection;
using System.Text.RegularExpressions;
using DbUp;
using Microsoft.Data.SqlClient;

// Applies the committed rw scripts to one database: the bootstrap script first (only when schema
// rw does not exist), then db/rw/V###__*.sql through DbUp, journaled in rw.SchemaVersions.
// Prints script names only, never the connection string.

const string BootstrapResource = "bootstrap/create_schema_and_journal.sql";
const string VersionedPrefix = "rw/V";

var envName = ReadConnectionEnvArgument(args);
if (envName is null)
{
    Console.Error.WriteLine("Usage: ResortWise.RwMigrator --connection-env <ENV VAR NAME>");
    return 2;
}

var connectionString = Environment.GetEnvironmentVariable(envName);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Environment variable {envName} is not set.");
    return 2;
}

var assembly = Assembly.GetExecutingAssembly();

using (var connection = new SqlConnection(connectionString))
{
    connection.Open();

    var schemaExists = ExecuteScalar(connection, "SELECT SCHEMA_ID(N'rw');") is not DBNull and not null;
    var journalExists = ExecuteScalar(connection, "SELECT OBJECT_ID(N'rw.SchemaVersions', N'U');") is not DBNull and not null;

    if (!schemaExists)
    {
        Console.WriteLine($"Schema rw not found. Running {BootstrapResource}.");
        RunBootstrap(connection, ReadResource(assembly, BootstrapResource));
    }
    else if (!journalExists)
    {
        Console.Error.WriteLine("Schema rw exists but rw.SchemaVersions does not. Stopping; nothing was changed.");
        return 1;
    }
}

var upgrader = DeployChanges.To
    .SqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(assembly, name => name.StartsWith(VersionedPrefix, StringComparison.Ordinal))
    .JournalToSqlTable("rw", "SchemaVersions")
    .WithTransactionPerScript()
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Console.Error.WriteLine($"Upgrade failed on script {result.ErrorScript?.Name ?? "(none)"}.");
    return 1;
}

Console.WriteLine("Done.");
return 0;

static string? ReadConnectionEnvArgument(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--connection-env" && !string.IsNullOrWhiteSpace(args[i + 1]))
        {
            return args[i + 1];
        }
    }

    return null;
}

static object? ExecuteScalar(SqlConnection connection, string sql)
{
    using var command = new SqlCommand(sql, connection);
    return command.ExecuteScalar();
}

static string ReadResource(Assembly assembly, string name)
{
    using var stream = assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded script {name} is missing.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static void RunBootstrap(SqlConnection connection, string script)
{
    var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
        .Where(batch => !string.IsNullOrWhiteSpace(batch));

    // One transaction, so the schema and the journal are created together or not at all.
    using var transaction = connection.BeginTransaction();
    foreach (var batch in batches)
    {
        using var command = new SqlCommand(batch, connection, transaction);
        command.ExecuteNonQuery();
    }

    transaction.Commit();
}
