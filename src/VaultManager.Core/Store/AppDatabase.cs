using Microsoft.Data.Sqlite;

namespace VaultManager.Core.Store;

public sealed record OperationRow(string Id, DateTimeOffset At, int ItemCount, bool Undone);

public sealed record CachedCatalogEntry(string Id, string Name, string Author, string Description, string Repo);

public sealed record OnlineVersion(string PluginId, string Version, DateTimeOffset CheckedAt);

/// <summary>A change that has not been able to run yet. The text is a ChangeItemJson form.</summary>
public sealed record PendingRow(string Id, string ItemJson);

/// <summary>
/// The app's own memory: journal, undo records, sync bases, ignored items, catalog cache.
/// It lives outside every vault and is never the truth about a vault; the disk is.
/// </summary>
public sealed class AppDatabase
{
	const int SchemaVersion = 2;
	readonly string _connectionString;

	public AppDatabase(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		_connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
		Migrate();
	}

	SqliteConnection Open()
	{
		var connection = new SqliteConnection(_connectionString);
		connection.Open();
		return connection;
	}

	void Migrate()
	{
		using SqliteConnection connection = Open();
		Execute(connection, "PRAGMA journal_mode=WAL;");
		long version = (long)Scalar(connection, "PRAGMA user_version;")!;
		if (version > SchemaVersion)
		{
			// A newer build wrote this file; guessing at its shape could destroy undo history.
			throw new InvalidOperationException($"Database schema {version} is newer than this build ({SchemaVersion}).");
		}

		if (version < 1)
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			Execute(connection, """
				CREATE TABLE operations(id TEXT PRIMARY KEY, at TEXT NOT NULL, undone INTEGER NOT NULL DEFAULT 0);
				CREATE TABLE operation_items(
					id INTEGER PRIMARY KEY AUTOINCREMENT,
					operation_id TEXT NOT NULL REFERENCES operations(id) ON DELETE CASCADE,
					undo_json TEXT NOT NULL);
				CREATE TABLE ignored(key TEXT PRIMARY KEY);
				CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
				CREATE TABLE sync_bases(source TEXT NOT NULL, target TEXT NOT NULL, path TEXT NOT NULL, hash TEXT NOT NULL, PRIMARY KEY(source, target, path));
				CREATE TABLE vault_profiles(vault_id TEXT PRIMARY KEY, profile_id TEXT NOT NULL);
				CREATE TABLE catalog(id TEXT PRIMARY KEY, name TEXT NOT NULL, author TEXT NOT NULL, description TEXT NOT NULL, repo TEXT NOT NULL);
				CREATE TABLE online_versions(plugin_id TEXT PRIMARY KEY, version TEXT NOT NULL, checked_at TEXT NOT NULL);
				PRAGMA user_version = 1;
				""", transaction);
			transaction.Commit();
		}

		if (version < 2)
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			Execute(connection, """
				CREATE TABLE pending_changes(seq INTEGER PRIMARY KEY AUTOINCREMENT, id TEXT NOT NULL UNIQUE, item_json TEXT NOT NULL);
				PRAGMA user_version = 2;
				""", transaction);
			transaction.Commit();
		}
	}

	static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null, params (string Name, object? Value)[] args)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = sql;
		command.Transaction = transaction;
		Bind(command, args);
		command.ExecuteNonQuery();
	}

	static object? Scalar(SqliteConnection connection, string sql, params (string Name, object? Value)[] args)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = sql;
		Bind(command, args);
		return command.ExecuteScalar();
	}

	static void Bind(SqliteCommand command, (string Name, object? Value)[] args)
	{
		foreach ((string name, object? value) in args)
		{
			command.Parameters.AddWithValue(name, value ?? DBNull.Value);
		}
	}

	static List<T> Query<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = sql;
		Bind(command, args);
		using SqliteDataReader reader = command.ExecuteReader();
		var rows = new List<T>();
		while (reader.Read())
		{
			rows.Add(map(reader));
		}

		return rows;
	}

	// Operations and undo

	/// <summary>The operation and all its undo records land together or not at all.</summary>
	public void SaveOperation(string id, DateTimeOffset at, IReadOnlyList<string> undoRecords)
	{
		using SqliteConnection connection = Open();
		using SqliteTransaction transaction = connection.BeginTransaction();
		Execute(connection, "INSERT INTO operations(id, at) VALUES ($id, $at)", transaction, ("$id", id), ("$at", at.ToString("O")));
		foreach (string record in undoRecords)
		{
			Execute(connection, "INSERT INTO operation_items(operation_id, undo_json) VALUES ($id, $json)", transaction, ("$id", id), ("$json", record));
		}

		transaction.Commit();
	}

	public OperationRow? LastUndoable()
	{
		using SqliteConnection connection = Open();
		return Query(connection, """
			SELECT o.id, o.at, COUNT(i.id), o.undone FROM operations o JOIN operation_items i ON i.operation_id = o.id
			WHERE o.undone = 0 GROUP BY o.id ORDER BY o.at DESC, o.rowid DESC LIMIT 1
			""", r => new OperationRow(r.GetString(0), DateTimeOffset.Parse(r.GetString(1)), r.GetInt32(2), r.GetInt64(3) != 0)).FirstOrDefault();
	}

	public IReadOnlyList<string> UndoRecords(string operationId)
	{
		using SqliteConnection connection = Open();
		return Query(connection, "SELECT undo_json FROM operation_items WHERE operation_id = $id ORDER BY id DESC", r => r.GetString(0), ("$id", operationId));
	}

	public void ReplaceUndoRecords(string operationId, IReadOnlyList<string> remaining)
	{
		using SqliteConnection connection = Open();
		using SqliteTransaction transaction = connection.BeginTransaction();
		Execute(connection, "DELETE FROM operation_items WHERE operation_id = $id", transaction, ("$id", operationId));
		foreach (string record in remaining.Reverse())
		{
			Execute(connection, "INSERT INTO operation_items(operation_id, undo_json) VALUES ($id, $json)", transaction, ("$id", operationId), ("$json", record));
		}

		if (remaining.Count == 0)
		{
			Execute(connection, "UPDATE operations SET undone = 1 WHERE id = $id", transaction, ("$id", operationId));
		}

		transaction.Commit();
	}

	/// <summary>Returns the ids of operations beyond the newest <paramref name="keep"/>, for snapshot pruning.</summary>
	public IReadOnlyList<string> PruneOperations(int keep)
	{
		using SqliteConnection connection = Open();
		List<string> old = Query(connection, "SELECT id FROM operations ORDER BY at DESC, rowid DESC LIMIT -1 OFFSET $keep", r => r.GetString(0), ("$keep", keep));
		foreach (string id in old)
		{
			Execute(connection, "DELETE FROM operation_items WHERE operation_id = $id", null, ("$id", id));
			Execute(connection, "DELETE FROM operations WHERE id = $id", null, ("$id", id));
		}

		return old;
	}

	// Waiting lines: they outlive the app and run when their gate opens.

	public void SavePending(string id, string itemJson)
	{
		using SqliteConnection connection = Open();
		Execute(connection, "INSERT INTO pending_changes(id, item_json) VALUES ($i, $j) ON CONFLICT(id) DO UPDATE SET item_json = $j", null, ("$i", id), ("$j", itemJson));
	}

	public void RemovePending(string id)
	{
		using SqliteConnection connection = Open();
		Execute(connection, "DELETE FROM pending_changes WHERE id = $i", null, ("$i", id));
	}

	public IReadOnlyList<PendingRow> PendingChanges()
	{
		using SqliteConnection connection = Open();
		return Query(connection, "SELECT id, item_json FROM pending_changes ORDER BY seq", r => new PendingRow(r.GetString(0), r.GetString(1)));
	}

	// Decisions

	public IReadOnlySet<string> Ignored()
	{
		using SqliteConnection connection = Open();
		return Query(connection, "SELECT key FROM ignored", r => r.GetString(0)).ToHashSet(StringComparer.Ordinal);
	}

	public void SetIgnored(string key, bool ignored)
	{
		using SqliteConnection connection = Open();
		Execute(connection, ignored ? "INSERT OR IGNORE INTO ignored(key) VALUES ($k)" : "DELETE FROM ignored WHERE key = $k", null, ("$k", key));
	}

	public string? Setting(string key)
	{
		using SqliteConnection connection = Open();
		return Scalar(connection, "SELECT value FROM settings WHERE key = $k", ("$k", key)) as string;
	}

	public void SetSetting(string key, string value)
	{
		using SqliteConnection connection = Open();
		Execute(connection, "INSERT INTO settings(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v", null, ("$k", key), ("$v", value));
	}

	public string? VaultProfile(string vaultId)
	{
		using SqliteConnection connection = Open();
		return Scalar(connection, "SELECT profile_id FROM vault_profiles WHERE vault_id = $v", ("$v", vaultId)) as string;
	}

	public void SetVaultProfile(string vaultId, string? profileId)
	{
		using SqliteConnection connection = Open();
		if (profileId is null)
		{
			Execute(connection, "DELETE FROM vault_profiles WHERE vault_id = $v", null, ("$v", vaultId));
		}
		else
		{
			Execute(connection, "INSERT INTO vault_profiles(vault_id, profile_id) VALUES ($v, $p) ON CONFLICT(vault_id) DO UPDATE SET profile_id = $p", null, ("$v", vaultId), ("$p", profileId));
		}
	}

	// Sync bases: the hash a file had when two vaults last agreed on it.

	public string? SyncBase(string source, string target, string path)
	{
		using SqliteConnection connection = Open();
		return Scalar(connection, "SELECT hash FROM sync_bases WHERE source = $s AND target = $t AND path = $p", ("$s", source), ("$t", target), ("$p", path)) as string;
	}

	public void SetSyncBase(string source, string target, string path, string hash)
	{
		using SqliteConnection connection = Open();
		Execute(connection, "INSERT INTO sync_bases(source, target, path, hash) VALUES ($s, $t, $p, $h) ON CONFLICT(source, target, path) DO UPDATE SET hash = $h",
			null, ("$s", source), ("$t", target), ("$p", path), ("$h", hash));
	}

	public void RestoreSyncBase(string source, string target, string path, string? hash)
	{
		if (hash is not null)
		{
			SetSyncBase(source, target, path, hash);
			return;
		}

		using SqliteConnection connection = Open();
		Execute(connection, "DELETE FROM sync_bases WHERE source = $s AND target = $t AND path = $p", null, ("$s", source), ("$t", target), ("$p", path));
	}

	// Catalog cache

	public void ReplaceCatalog(IReadOnlyList<CachedCatalogEntry> entries)
	{
		using SqliteConnection connection = Open();
		using SqliteTransaction transaction = connection.BeginTransaction();
		Execute(connection, "DELETE FROM catalog", transaction);
		foreach (CachedCatalogEntry e in entries)
		{
			Execute(connection, "INSERT OR REPLACE INTO catalog(id, name, author, description, repo) VALUES ($i, $n, $a, $d, $r)", transaction,
				("$i", e.Id), ("$n", e.Name), ("$a", e.Author), ("$d", e.Description), ("$r", e.Repo));
		}

		transaction.Commit();
	}

	public IReadOnlyList<CachedCatalogEntry> Catalog()
	{
		using SqliteConnection connection = Open();
		return Query(connection, "SELECT id, name, author, description, repo FROM catalog ORDER BY name", r => new CachedCatalogEntry(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
	}

	public void SaveOnlineVersions(IReadOnlyList<OnlineVersion> versions)
	{
		using SqliteConnection connection = Open();
		using SqliteTransaction transaction = connection.BeginTransaction();
		foreach (OnlineVersion v in versions)
		{
			Execute(connection, "INSERT INTO online_versions(plugin_id, version, checked_at) VALUES ($p, $v, $c) ON CONFLICT(plugin_id) DO UPDATE SET version = $v, checked_at = $c", transaction,
				("$p", v.PluginId), ("$v", v.Version), ("$c", v.CheckedAt.ToString("O")));
		}

		transaction.Commit();
	}

	public IReadOnlyList<OnlineVersion> OnlineVersions()
	{
		using SqliteConnection connection = Open();
		return Query(connection, "SELECT plugin_id, version, checked_at FROM online_versions", r => new OnlineVersion(r.GetString(0), r.GetString(1), DateTimeOffset.Parse(r.GetString(2))));
	}
}
