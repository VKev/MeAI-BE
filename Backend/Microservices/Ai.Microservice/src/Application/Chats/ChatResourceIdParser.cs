using System.Text.Json;

namespace Application.Chats;

internal static class ChatResourceIdParser
{
    public static List<Guid> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<Guid>();
        }

        try
        {
            var stringIds = JsonSerializer.Deserialize<List<string>>(json);
            if (stringIds is null || stringIds.Count == 0)
            {
                return new List<Guid>();
            }

            return stringIds
                .Select(value => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToList();
        }
        catch (JsonException)
        {
            try
            {
                var guidIds = JsonSerializer.Deserialize<List<Guid>>(json);
                return guidIds?.Where(id => id != Guid.Empty).ToList() ?? new List<Guid>();
            }
            catch (JsonException)
            {
                return new List<Guid>();
            }
        }
    }
}
