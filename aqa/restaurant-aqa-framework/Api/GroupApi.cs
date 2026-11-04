using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Restaurant.Aqa.Configuration;

namespace Restaurant.Aqa.Api;

public sealed class GroupApi : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly HttpClient client = new()
    {
        BaseAddress = new TestEnvironment().ApiBaseUrl,
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<string> SignInAsync(TestAccount account)
    {
        using var response = await SendAsync(HttpMethod.Post, "signin", body: new { email = account.Email, password = account.Password });
        RequireStatus(response, HttpStatusCode.OK, "Sign in");
        var tokens = await ReadAsync<SignInResult>(response);
        if (string.IsNullOrWhiteSpace(tokens.IdToken)) throw new InvalidOperationException("Sign-in returned no ID token.");
        return tokens.IdToken;
    }

    public async Task<T> GetAsync<T>(string path, string? token = null)
    {
        using var response = await SendAsync(HttpMethod.Get, path, token);
        var operation = path.StartsWith("guest/invites/", StringComparison.Ordinal)
            ? "GET guest invitation" : $"GET {path}";
        RequireStatus(response, HttpStatusCode.OK, operation);
        return await ReadAsync<T>(response);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    public Task<HttpResponseMessage> ParticipantsResponseAsync(string groupId, string? token) =>
        SendAsync(HttpMethod.Get, $"guest/groups/{Uri.EscapeDataString(groupId)}/sessions", token);

    public Task<ParticipantsSnapshot> ParticipantsAsync(string groupId, string token) =>
        GetAsync<ParticipantsSnapshot>($"guest/groups/{Uri.EscapeDataString(groupId)}/sessions", token);

    public async Task<GuestIdentity> JoinAsync(string groupId, string name)
    {
        using var response = await SendAsync(HttpMethod.Post, "guest/sessions", body: new { GroupReservationId = groupId, GuestName = name });
        RequireStatus(response, HttpStatusCode.OK, "Create guest session");
        return await ReadAsync<GuestIdentity>(response);
    }

    public async Task AddDishAsync(GuestIdentity guest, string dishId, int quantity)
    {
        using var response = await SendAsync(HttpMethod.Post, $"guest/sessions/{Uri.EscapeDataString(guest.SessionId)}/dishes",
            guest.SessionToken, new { dishId, quantity });
        RequireStatus(response, HttpStatusCode.OK, "Add guest dish");
    }

    public async Task SubmitGuestAsync(GuestIdentity guest)
    {
        using var response = await SendAsync(HttpMethod.Post, $"guest/sessions/{Uri.EscapeDataString(guest.SessionId)}/submit", guest.SessionToken);
        RequireStatus(response, HttpStatusCode.OK, "Submit guest cart");
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(Json)
        ?? throw new InvalidOperationException($"API returned no {typeof(T).Name} payload.");

    public static void RequireStatus(HttpResponseMessage response, HttpStatusCode expected, string operation)
    {
        // Do not include request/response bodies: login and guest-session responses contain secrets.
        if (response.StatusCode != expected)
            throw new InvalidOperationException($"{operation}: expected HTTP {(int)expected}, received {(int)response.StatusCode}.");
    }

    public void Dispose() => client.Dispose();
}

public sealed class SignInResult { public string IdToken { get; set; } = ""; }
public sealed class GuestIdentity
{
    public string SessionId { get; set; } = "";
    public string SessionToken { get; set; } = "";
    public override string ToString() => "Guest session";
}
public sealed record AppUser(string Id, string FirstName, string LastName, string Role);
public sealed record WaiterLocation(string LocationId);
public sealed record RestaurantTable(string Id, string LocationId, int Capacity, int TableNumber);
public sealed record TestDish(string Id, string Name, decimal Price, string State);
public sealed record ReservationResult(string Id);
public sealed record InviteToken(string Token);
public sealed record GroupInvite(string GroupReservationId);
public sealed record ParticipantsSnapshot(ParticipantSnapshot[] Participants, decimal Total);
public sealed record ParticipantSnapshot(string Id, string Type, string Name, BasketItem[] Items, decimal Total, bool IsSubmitted);
public sealed record BasketItem(TestDish Dish, int Quantity);
