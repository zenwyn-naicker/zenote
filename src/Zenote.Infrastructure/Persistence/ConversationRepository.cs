using System.Text.Json;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.Infrastructure.Persistence;

public sealed class ConversationRepository(SqliteConnectionFactory connections) : IConversationRepository
{
    public async Task<Conversation> CreateAsync(Guid notebookId, string title, CancellationToken ct)
    {
        var conversation = new Conversation(Guid.NewGuid(), notebookId, title, DateTimeOffset.UtcNow);

        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO conversations(id, notebook_id, title, updated_at) VALUES ($id, $notebook, $title, $updated)";
        command.Parameters.AddWithValue("$id", conversation.Id.ToString());
        command.Parameters.AddWithValue("$notebook", notebookId.ToString());
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$updated", conversation.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);

        return conversation;
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(Guid notebookId, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, updated_at FROM conversations WHERE notebook_id = $notebook ORDER BY updated_at DESC";
        command.Parameters.AddWithValue("$notebook", notebookId.ToString());

        var conversations = new List<Conversation>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            conversations.Add(new Conversation(
                Guid.Parse(reader.GetString(0)), notebookId, reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2))));
        }

        return conversations;
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(Guid conversationId, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, role, content, not_found, citations_json, created_at
            FROM messages WHERE conversation_id = $conversation ORDER BY sequence
            """;
        command.Parameters.AddWithValue("$conversation", conversationId.ToString());

        var messages = new List<Message>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            messages.Add(new Message(
                Guid.Parse(reader.GetString(0)),
                conversationId,
                Enum.Parse<MessageRole>(reader.GetString(1)),
                reader.GetString(2),
                reader.GetBoolean(3),
                JsonSerializer.Deserialize<List<Citation>>(reader.GetString(4)) ?? [],
                DateTimeOffset.Parse(reader.GetString(5))));
        }

        return messages;
    }

    public async Task<Guid?> GetLastViewedAsync(Guid notebookId, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_conversation_id FROM notebooks WHERE id = $notebook";
        command.Parameters.AddWithValue("$notebook", notebookId.ToString());

        return await command.ExecuteScalarAsync(ct) is string id ? Guid.Parse(id) : null;
    }

    public async Task SetLastViewedAsync(Guid notebookId, Guid conversationId, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE notebooks SET last_conversation_id = $conversation WHERE id = $notebook";
        command.Parameters.AddWithValue("$notebook", notebookId.ToString());
        command.Parameters.AddWithValue("$conversation", conversationId.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task AppendMessageAsync(Message message, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO messages(id, conversation_id, role, content, not_found, citations_json, created_at)
            VALUES ($id, $conversation, $role, $content, $notFound, $citations, $created);
            UPDATE conversations SET updated_at = $created WHERE id = $conversation;
            """;
        command.Parameters.AddWithValue("$id", message.Id.ToString());
        command.Parameters.AddWithValue("$conversation", message.ConversationId.ToString());
        command.Parameters.AddWithValue("$role", message.Role.ToString());
        command.Parameters.AddWithValue("$content", message.Content);
        command.Parameters.AddWithValue("$notFound", message.NotFound);
        command.Parameters.AddWithValue("$citations", JsonSerializer.Serialize(message.Citations));
        command.Parameters.AddWithValue("$created", message.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
        transaction.Commit();
    }

    public async Task RenameAsync(Guid id, string title, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE conversations SET title = $title WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$title", title);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM conversations WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }
}
