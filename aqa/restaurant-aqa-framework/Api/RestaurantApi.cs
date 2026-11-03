using Restaurant.Aqa.Configuration;

namespace Restaurant.Aqa.Api;

public sealed class RestaurantApi : IDisposable
{
    private readonly HttpClient client = new()
    {
        BaseAddress = new TestEnvironment().ApiBaseUrl,
        Timeout = TimeSpan.FromSeconds(30)
    };

    public Task<HttpResponseMessage> GetDishesAsync() => client.GetAsync("dishes");

    public Task<HttpResponseMessage> GetParticipantsAnonymouslyAsync(string groupId) =>
        client.GetAsync($"guest/groups/{Uri.EscapeDataString(groupId)}/sessions");

    public void Dispose() => client.Dispose();
}
