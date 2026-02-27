using System.Text.Json;
using ElrsTtlBatchFlasher.Models;

namespace ElrsTtlBatchFlasher.Services;

public static class ProfilesService
{
    public static List<ReceiverProfile> LoadProfiles(string baseDir)
    {
        var path = Path.Combine(baseDir, "receivers.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("receivers.json not found next to the app.", path);

        var json = File.ReadAllText(path);
        var profiles = JsonSerializer.Deserialize<List<ReceiverProfile>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return profiles ?? new List<ReceiverProfile>();
    }
}
