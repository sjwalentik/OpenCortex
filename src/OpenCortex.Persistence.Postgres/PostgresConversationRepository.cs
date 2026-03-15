using Npgsql;
using OpenCortex.Conversations;

namespace OpenCortex.Persistence.Postgres;

public sealed class PostgresConversationRepository : IConversationRepository
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresConversationRepository(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Conversation> CreateAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.conversations (
                conversation_id,
                brain_id,
                customer_id,
                user_id,
                title,
                system_prompt,
                status,
                metadata,
                created_at,
                last_message_at
            ) VALUES (
                @conversation_id,
                @brain_id,
                @customer_id,
                @user_id,
                @title,
                @system_prompt,
                @status,
                @metadata::jsonb,
                @created_at,
                @last_message_at
            )
            RETURNING conversation_id, created_at, updated_at;
            """;

        command.Parameters.AddWithValue("conversation_id", conversation.ConversationId);
        command.Parameters.AddWithValue("brain_id", (object?)conversation.BrainId ?? DBNull.Value);
        command.Parameters.AddWithValue("customer_id", conversation.CustomerId);
        command.Parameters.AddWithValue("user_id", (object?)conversation.UserId ?? DBNull.Value);
        command.Parameters.AddWithValue("title", (object?)conversation.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("system_prompt", (object?)conversation.SystemPrompt ?? DBNull.Value);
        command.Parameters.AddWithValue("status", conversation.Status.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("metadata", (object?)conversation.Metadata ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", conversation.CreatedAt);
        command.Parameters.AddWithValue("last_message_at", (object?)conversation.LastMessageAt ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            conversation.ConversationId = reader.GetString(0);
            conversation.CreatedAt = reader.GetDateTime(1);
        }

        return conversation;
    }

    public async Task<Conversation?> GetByIdAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT
                conversation_id,
                brain_id,
                customer_id,
                user_id,
                title,
                system_prompt,
                status,
                metadata,
                created_at,
                last_message_at
            FROM {_connectionFactory.Schema}.conversations
            WHERE conversation_id = @conversation_id;
            """;

        command.Parameters.AddWithValue("conversation_id", conversationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapConversation(reader);
        }

        return null;
    }

    public async Task<Conversation?> GetWithMessagesAsync(string conversationId, int? messageLimit = null, CancellationToken cancellationToken = default)
    {
        var conversation = await GetByIdAsync(conversationId, cancellationToken);
        if (conversation is null) return null;

        conversation.Messages = (await GetMessagesAsync(conversationId, messageLimit, cancellationToken: cancellationToken)).ToList();
        return conversation;
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(
        string customerId,
        ConversationStatus? status = null,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var whereClause = "WHERE customer_id = @customer_id";
        if (status.HasValue)
        {
            whereClause += " AND status = @status";
        }

        command.CommandText = $"""
            SELECT
                conversation_id,
                brain_id,
                customer_id,
                user_id,
                title,
                system_prompt,
                status,
                metadata,
                created_at,
                last_message_at
            FROM {_connectionFactory.Schema}.conversations
            {whereClause}
            ORDER BY last_message_at DESC NULLS LAST, created_at DESC
            LIMIT @limit OFFSET @offset;
            """;

        command.Parameters.AddWithValue("customer_id", customerId);
        if (status.HasValue)
        {
            command.Parameters.AddWithValue("status", status.Value.ToString().ToLowerInvariant());
        }
        command.Parameters.AddWithValue("limit", limit ?? 50);
        command.Parameters.AddWithValue("offset", offset ?? 0);

        var conversations = new List<Conversation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            conversations.Add(MapConversation(reader));
        }

        return conversations;
    }

    public async Task UpdateAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.conversations
            SET
                title = @title,
                system_prompt = @system_prompt,
                status = @status,
                metadata = @metadata::jsonb,
                last_message_at = @last_message_at,
                updated_at = now()
            WHERE conversation_id = @conversation_id;
            """;

        command.Parameters.AddWithValue("conversation_id", conversation.ConversationId);
        command.Parameters.AddWithValue("title", (object?)conversation.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("system_prompt", (object?)conversation.SystemPrompt ?? DBNull.Value);
        command.Parameters.AddWithValue("status", conversation.Status.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("metadata", (object?)conversation.Metadata ?? DBNull.Value);
        command.Parameters.AddWithValue("last_message_at", (object?)conversation.LastMessageAt ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.conversations
            SET status = 'archived', updated_at = now()
            WHERE conversation_id = @conversation_id;
            """;

        command.Parameters.AddWithValue("conversation_id", conversationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Message> AddMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            INSERT INTO {_connectionFactory.Schema}.messages (
                message_id,
                conversation_id,
                parent_message_id,
                role,
                content,
                provider_id,
                model_id,
                tool_calls,
                token_usage,
                latency_ms,
                metadata,
                created_at
            ) VALUES (
                @message_id,
                @conversation_id,
                @parent_message_id,
                @role,
                @content,
                @provider_id,
                @model_id,
                @tool_calls::jsonb,
                @token_usage::jsonb,
                @latency_ms,
                @metadata::jsonb,
                @created_at
            )
            RETURNING message_id, created_at;
            """;

        command.Parameters.AddWithValue("message_id", message.MessageId);
        command.Parameters.AddWithValue("conversation_id", message.ConversationId);
        command.Parameters.AddWithValue("parent_message_id", (object?)message.ParentMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("role", message.Role.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("content", (object?)message.Content ?? DBNull.Value);
        command.Parameters.AddWithValue("provider_id", (object?)message.ProviderId ?? DBNull.Value);
        command.Parameters.AddWithValue("model_id", (object?)message.ModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("tool_calls", (object?)message.ToolCallsJson ?? DBNull.Value);
        command.Parameters.AddWithValue("token_usage", (object?)message.TokenUsageJson ?? DBNull.Value);
        command.Parameters.AddWithValue("latency_ms", (object?)message.LatencyMs ?? DBNull.Value);
        command.Parameters.AddWithValue("metadata", (object?)message.Metadata ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", message.CreatedAt);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            message.MessageId = reader.GetString(0);
            message.CreatedAt = reader.GetDateTime(1);
        }

        return message;
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(
        string conversationId,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT
                message_id,
                conversation_id,
                parent_message_id,
                role,
                content,
                provider_id,
                model_id,
                tool_calls,
                token_usage,
                latency_ms,
                metadata,
                created_at
            FROM {_connectionFactory.Schema}.messages
            WHERE conversation_id = @conversation_id
            ORDER BY created_at ASC
            LIMIT @limit OFFSET @offset;
            """;

        command.Parameters.AddWithValue("conversation_id", conversationId);
        command.Parameters.AddWithValue("limit", limit ?? 1000);
        command.Parameters.AddWithValue("offset", offset ?? 0);

        var messages = new List<Message>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(MapMessage(reader));
        }

        return messages;
    }

    public async Task UpdateMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            UPDATE {_connectionFactory.Schema}.messages
            SET
                content = @content,
                metadata = @metadata::jsonb
            WHERE message_id = @message_id;
            """;

        command.Parameters.AddWithValue("message_id", message.MessageId);
        command.Parameters.AddWithValue("content", (object?)message.Content ?? DBNull.Value);
        command.Parameters.AddWithValue("metadata", (object?)message.Metadata ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountAsync(string customerId, ConversationStatus? status = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var whereClause = "WHERE customer_id = @customer_id";
        if (status.HasValue)
        {
            whereClause += " AND status = @status";
        }

        command.CommandText = $"""
            SELECT COUNT(*) FROM {_connectionFactory.Schema}.conversations {whereClause};
            """;

        command.Parameters.AddWithValue("customer_id", customerId);
        if (status.HasValue)
        {
            command.Parameters.AddWithValue("status", status.Value.ToString().ToLowerInvariant());
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private static Conversation MapConversation(NpgsqlDataReader reader)
    {
        return new Conversation
        {
            ConversationId = reader.GetString(0),
            BrainId = reader.IsDBNull(1) ? null : reader.GetString(1),
            CustomerId = reader.GetString(2),
            UserId = reader.IsDBNull(3) ? null : reader.GetString(3),
            Title = reader.IsDBNull(4) ? null : reader.GetString(4),
            SystemPrompt = reader.IsDBNull(5) ? null : reader.GetString(5),
            Status = Enum.Parse<ConversationStatus>(reader.GetString(6), ignoreCase: true),
            Metadata = reader.IsDBNull(7) ? null : reader.GetString(7),
            CreatedAt = reader.GetDateTime(8),
            LastMessageAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9)
        };
    }

    private static Message MapMessage(NpgsqlDataReader reader)
    {
        return new Message
        {
            MessageId = reader.GetString(0),
            ConversationId = reader.GetString(1),
            ParentMessageId = reader.IsDBNull(2) ? null : reader.GetString(2),
            Role = Enum.Parse<MessageRole>(reader.GetString(3), ignoreCase: true),
            Content = reader.IsDBNull(4) ? null : reader.GetString(4),
            ProviderId = reader.IsDBNull(5) ? null : reader.GetString(5),
            ModelId = reader.IsDBNull(6) ? null : reader.GetString(6),
            ToolCallsJson = reader.IsDBNull(7) ? null : reader.GetString(7),
            TokenUsageJson = reader.IsDBNull(8) ? null : reader.GetString(8),
            LatencyMs = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            Metadata = reader.IsDBNull(10) ? null : reader.GetString(10),
            CreatedAt = reader.GetDateTime(11)
        };
    }
}
