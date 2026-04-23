using Microsoft.Data.Sqlite;

namespace DotCraft.State;

public sealed class StateRuntime
{
    private readonly string _connectionString;
    private readonly object _initLock = new();
    private bool _initialized;

    public StateRuntime(string botPath)
    {
        Directory.CreateDirectory(botPath);
        DbPath = Path.Combine(botPath, "state.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        EnsureInitialized();
    }

    public string DbPath { get; }

    public SqliteConnection OpenConnection()
    {
        EnsureInitialized();
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA foreign_keys=ON;
            """;
        pragma.ExecuteNonQuery();
        return connection;
    }

    public string? GetInfo(string key)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM state_info WHERE key = $key LIMIT 1";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetInfo(string key, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO state_info(key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_initLock)
        {
            if (_initialized)
                return;

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA foreign_keys=ON;

                CREATE TABLE IF NOT EXISTS state_info (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS threads (
                    thread_id TEXT PRIMARY KEY,
                    rollout_path TEXT NOT NULL,
                    workspace_path TEXT NOT NULL,
                    user_id TEXT,
                    origin_channel TEXT NOT NULL,
                    channel_context TEXT,
                    display_name TEXT,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    archived_at TEXT,
                    history_mode TEXT NOT NULL,
                    turn_count INTEGER NOT NULL DEFAULT 0,
                    first_user_message TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_threads_updated_at ON threads(updated_at DESC, thread_id DESC);
                CREATE INDEX IF NOT EXISTS idx_threads_workspace_identity
                    ON threads(workspace_path, user_id, channel_context, origin_channel);
                CREATE INDEX IF NOT EXISTS idx_threads_status ON threads(status);

                CREATE TABLE IF NOT EXISTS thread_sessions (
                    thread_id TEXT PRIMARY KEY,
                    session_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS trace_sessions (
                    session_key TEXT PRIMARY KEY,
                    started_at TEXT NOT NULL,
                    last_activity_at TEXT NOT NULL,
                    request_count INTEGER NOT NULL DEFAULT 0,
                    response_count INTEGER NOT NULL DEFAULT 0,
                    tool_call_count INTEGER NOT NULL DEFAULT 0,
                    error_count INTEGER NOT NULL DEFAULT 0,
                    context_compaction_count INTEGER NOT NULL DEFAULT 0,
                    thinking_count INTEGER NOT NULL DEFAULT 0,
                    total_input_tokens INTEGER NOT NULL DEFAULT 0,
                    total_output_tokens INTEGER NOT NULL DEFAULT 0,
                    total_tool_duration_ms INTEGER NOT NULL DEFAULT 0,
                    max_tool_duration_ms INTEGER NOT NULL DEFAULT 0,
                    last_finish_reason TEXT,
                    final_system_prompt TEXT,
                    tool_names_json TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_trace_sessions_last_activity
                    ON trace_sessions(last_activity_at DESC, session_key DESC);

                CREATE TABLE IF NOT EXISTS trace_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_id TEXT NOT NULL,
                    session_key TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    type TEXT NOT NULL,
                    tool_name TEXT,
                    call_id TEXT,
                    response_id TEXT,
                    message_id TEXT,
                    model_id TEXT,
                    finish_reason TEXT,
                    duration_ms REAL,
                    event_json TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_trace_events_session_ts
                    ON trace_events(session_key, timestamp, id);

                CREATE TABLE IF NOT EXISTS trace_session_bindings (
                    session_key TEXT PRIMARY KEY,
                    root_thread_id TEXT,
                    parent_session_key TEXT,
                    binding_kind TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_trace_bindings_root_thread
                    ON trace_session_bindings(root_thread_id, session_key);
                CREATE INDEX IF NOT EXISTS idx_trace_bindings_parent_session
                    ON trace_session_bindings(parent_session_key, session_key);
                CREATE INDEX IF NOT EXISTS idx_trace_bindings_kind
                    ON trace_session_bindings(binding_kind, session_key);

                CREATE TABLE IF NOT EXISTS token_usage_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    channel TEXT NOT NULL,
                    user_id TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    group_id INTEGER,
                    group_name TEXT,
                    input_tokens INTEGER NOT NULL,
                    output_tokens INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_token_usage_channel_ts
                    ON token_usage_records(channel, timestamp DESC, id DESC);

                CREATE TABLE IF NOT EXISTS dashboard_usage_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    source_id TEXT NOT NULL,
                    source_mode TEXT NOT NULL,
                    subject_kind TEXT NOT NULL,
                    subject_id TEXT NOT NULL,
                    subject_label TEXT NOT NULL,
                    context_kind TEXT,
                    context_id TEXT,
                    context_label TEXT,
                    thread_id TEXT,
                    session_key TEXT,
                    input_tokens INTEGER NOT NULL,
                    output_tokens INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_dashboard_usage_source_ts
                    ON dashboard_usage_records(source_id, timestamp DESC, id DESC);
                CREATE INDEX IF NOT EXISTS idx_dashboard_usage_source_subject
                    ON dashboard_usage_records(source_id, subject_kind, subject_id);
                CREATE INDEX IF NOT EXISTS idx_dashboard_usage_source_context
                    ON dashboard_usage_records(source_id, context_kind, context_id);
                CREATE INDEX IF NOT EXISTS idx_dashboard_usage_thread
                    ON dashboard_usage_records(thread_id, timestamp DESC, id DESC);
                CREATE INDEX IF NOT EXISTS idx_dashboard_usage_session
                    ON dashboard_usage_records(session_key, timestamp DESC, id DESC);
                """;
            command.ExecuteNonQuery();

            _initialized = true;
        }
    }
}
