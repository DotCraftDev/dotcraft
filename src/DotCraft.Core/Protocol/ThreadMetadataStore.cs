using System.Text.Json;
using DotCraft.State;
using Microsoft.Data.Sqlite;

namespace DotCraft.Protocol;

internal sealed class ThreadMetadataStore(StateRuntime stateRuntime)
{
    public void UpsertThread(SessionThread thread, string rolloutPath)
    {
        var summary = ThreadSummary.FromThread(thread);
        var firstUserMessage = ExtractFirstUserMessage(thread);
        var archivedAt = thread.Status == ThreadStatus.Archived ? thread.LastActiveAt : (DateTimeOffset?)null;

        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO threads (
                thread_id,
                rollout_path,
                workspace_path,
                user_id,
                origin_channel,
                channel_context,
                display_name,
                status,
                created_at,
                updated_at,
                archived_at,
                history_mode,
                turn_count,
                first_user_message
            ) VALUES (
                $thread_id,
                $rollout_path,
                $workspace_path,
                $user_id,
                $origin_channel,
                $channel_context,
                $display_name,
                $status,
                $created_at,
                $updated_at,
                $archived_at,
                $history_mode,
                $turn_count,
                $first_user_message
            )
            ON CONFLICT(thread_id) DO UPDATE SET
                rollout_path = excluded.rollout_path,
                workspace_path = excluded.workspace_path,
                user_id = excluded.user_id,
                origin_channel = excluded.origin_channel,
                channel_context = excluded.channel_context,
                display_name = excluded.display_name,
                status = excluded.status,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                archived_at = excluded.archived_at,
                history_mode = excluded.history_mode,
                turn_count = excluded.turn_count,
                first_user_message = excluded.first_user_message
            """;
        command.Parameters.AddWithValue("$thread_id", thread.Id);
        command.Parameters.AddWithValue("$rollout_path", rolloutPath);
        command.Parameters.AddWithValue("$workspace_path", summary.WorkspacePath);
        command.Parameters.AddWithValue("$user_id", (object?)summary.UserId ?? DBNull.Value);
        command.Parameters.AddWithValue("$origin_channel", summary.OriginChannel);
        command.Parameters.AddWithValue("$channel_context", (object?)summary.ChannelContext ?? DBNull.Value);
        command.Parameters.AddWithValue("$display_name", (object?)summary.DisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", summary.Status.ToString());
        command.Parameters.AddWithValue("$created_at", summary.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", summary.LastActiveAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$archived_at", archivedAt?.UtcDateTime.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$history_mode", thread.HistoryMode.ToString());
        command.Parameters.AddWithValue("$turn_count", summary.TurnCount);
        command.Parameters.AddWithValue("$first_user_message", (object?)firstUserMessage ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public List<ThreadSummary> LoadIndex()
    {
        var list = new List<ThreadSummary>();
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                thread_id,
                user_id,
                workspace_path,
                origin_channel,
                channel_context,
                display_name,
                status,
                created_at,
                updated_at,
                turn_count
            FROM threads
            ORDER BY updated_at DESC, thread_id DESC
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ThreadSummary
            {
                Id = reader.GetString(0),
                UserId = reader.IsDBNull(1) ? null : reader.GetString(1),
                WorkspacePath = reader.GetString(2),
                OriginChannel = reader.GetString(3),
                ChannelContext = reader.IsDBNull(4) ? null : reader.GetString(4),
                DisplayName = reader.IsDBNull(5) ? null : reader.GetString(5),
                Status = Enum.TryParse<ThreadStatus>(reader.GetString(6), out var status) ? status : ThreadStatus.Active,
                CreatedAt = DateTimeOffset.Parse(reader.GetString(7)),
                LastActiveAt = DateTimeOffset.Parse(reader.GetString(8)),
                TurnCount = reader.GetInt32(9)
            });
        }

        return list;
    }

    public string? GetRolloutPath(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rollout_path FROM threads WHERE thread_id = $thread_id LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", threadId);
        return command.ExecuteScalar() as string;
    }

    public List<ThreadRolloutLocation> LoadRolloutLocations()
    {
        var list = new List<ThreadRolloutLocation>();
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT thread_id, rollout_path, status
            FROM threads
            ORDER BY updated_at DESC, thread_id DESC
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ThreadRolloutLocation(
                reader.GetString(0),
                reader.GetString(1),
                Enum.TryParse<ThreadStatus>(reader.GetString(2), out var status) ? status : ThreadStatus.Active));
        }

        return list;
    }

    public void UpdateRolloutPath(string threadId, string rolloutPath)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE threads
            SET rollout_path = $rollout_path
            WHERE thread_id = $thread_id
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$rollout_path", rolloutPath);
        command.ExecuteNonQuery();
    }

    public void DeleteThread(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM threads WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.ExecuteNonQuery();
    }

    public void SaveSessionJson(string threadId, string sessionJson)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_sessions(thread_id, session_json, updated_at)
            VALUES ($thread_id, $session_json, $updated_at)
            ON CONFLICT(thread_id) DO UPDATE SET
                session_json = excluded.session_json,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$session_json", sessionJson);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    public string? LoadSessionJson(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_json FROM thread_sessions WHERE thread_id = $thread_id LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", threadId);
        return command.ExecuteScalar() as string;
    }

    public bool SessionExists(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM thread_sessions WHERE thread_id = $thread_id LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", threadId);
        return command.ExecuteScalar() != null;
    }

    public void DeleteSession(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM thread_sessions WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.ExecuteNonQuery();
    }

    private static string? ExtractFirstUserMessage(SessionThread thread)
    {
        foreach (var turn in thread.Turns)
        {
            foreach (var item in turn.Items)
            {
                if (item.Type != ItemType.UserMessage)
                    continue;

                if (item.Payload is UserMessagePayload payload && !string.IsNullOrWhiteSpace(payload.Text))
                    return payload.Text.Trim();

                if (item.Payload is JsonElement element
                    && element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("text", out var textElement))
                {
                    var text = textElement.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
            }
        }

        return null;
    }
}

internal sealed record ThreadRolloutLocation(string ThreadId, string RolloutPath, ThreadStatus Status);
