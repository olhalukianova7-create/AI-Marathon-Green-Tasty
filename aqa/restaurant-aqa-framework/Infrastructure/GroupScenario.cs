using System.Net;
using Restaurant.Aqa.Api;
using Restaurant.Aqa.Configuration;
using Xunit.Abstractions;

namespace Restaurant.Aqa.Infrastructure;

public sealed class GroupScenario : IAsyncDisposable
{
    public GroupApi Api { get; } = new();
    public TestEnvironment Environment { get; } = new();
    public string OwnerToken { get; private set; } = "";
    public string OtherToken { get; private set; } = "";
    public string WaiterToken { get; private set; } = "";
    public string ReservationId { get; private set; } = "";
    public string GroupId { get; private set; } = "";
    public string InviteToken { get; private set; } = "";
    public TestDish Dish { get; private set; } = null!;
    public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];
    private readonly ITestOutputHelper output;
    private readonly List<string> guestIds = [];

    private GroupScenario(ITestOutputHelper output) => this.output = output;

    public static async Task<GroupScenario> CreateAsync(ITestOutputHelper output)
    {
        var scenario = new GroupScenario(output);
        try { await scenario.PrepareAsync(); return scenario; }
        catch
        {
            // Cleanup also runs if setup failed after the reservation was created.
            try { await scenario.DisposeAsync(); }
            catch (Exception cleanup) { output.WriteLine($"Setup cleanup failed: {cleanup.Message}"); }
            throw;
        }
    }

    private async Task PrepareAsync()
    {
        OwnerToken = await Api.SignInAsync(Environment.Account("OWNER"));
        OtherToken = await Api.SignInAsync(Environment.Account("OTHER_CUSTOMER"));
        WaiterToken = await Api.SignInAsync(Environment.Account("WAITER"));
        var owner = await Api.GetAsync<AppUser>("me", OwnerToken);
        var other = await Api.GetAsync<AppUser>("me", OtherToken);
        var waiter = await Api.GetAsync<AppUser>("me", WaiterToken);
        if (owner.Role != "Customer" || other.Role != "Customer" || waiter.Role != "Waiter"
            || new[] { owner.Id, other.Id, waiter.Id }.Distinct().Count() != 3)
            throw new InvalidOperationException("Configure three distinct accounts: customer owner, another customer, and assigned waiter.");

        var location = await Api.GetAsync<WaiterLocation>($"waiters/{Uri.EscapeDataString(waiter.Id)}/location", WaiterToken);
        var dishes = await Api.GetAsync<TestDish[]>($"locations/{Uri.EscapeDataString(location.LocationId)}/dishes");
        Dish = dishes.FirstOrDefault(d => d.State == "Available" && d.Price > 0)
            ?? throw new InvalidOperationException("The waiter's restaurant needs an available dish with a positive price.");

        // Discover eligible tables once using the public endpoint already supported
        // by this deployment. Reservation creation validates each exact future slot.
        // The waiter-only inventory route cannot be used by the deployed auth setup.
        var inventoryStart = DateTime.UtcNow.Date.AddDays(91).AddHours(12);
        var restaurantTables = await Api.GetAsync<RestaurantTable[]>(
            $"available-tables?locationId={Uri.EscapeDataString(location.LocationId)}&capacity=4&startTime={Uri.EscapeDataString(inventoryStart.ToString("O"))}");
        var tables = restaurantTables.Where(t => t.Capacity >= 4).ToArray();
        if (tables.Length == 0)
            throw new InvalidOperationException("The waiter's restaurant needs a table with capacity at least four.");

        var candidateDays = Enumerable.Range(7, 84).OrderBy(_ => Random.Shared.Next()).Take(20).ToArray();
        var overlapConflicts = 0;
        foreach (var daysAhead in candidateDays)
        {
            var start = DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(12)
                .AddMinutes(Random.Shared.Next(0, 180));
            foreach (var table in tables.OrderBy(_ => Random.Shared.Next()))
            {
                using var response = await Api.SendAsync(HttpMethod.Post, "reservations", OwnerToken,
                    new { tableId = table.Id, startTime = start, endTime = start.AddMinutes(90), guestCount = 4 });
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    if (error.Contains("overlap", StringComparison.OrdinalIgnoreCase))
                    {
                        overlapConflicts++;
                        continue;
                    }
                }
                GroupApi.RequireStatus(response, HttpStatusCode.Created, "Create test reservation");
                ReservationId = (await GroupApi.ReadAsync<ReservationResult>(response)).Id;
                break;
            }
            if (!string.IsNullOrWhiteSpace(ReservationId)) break;
        }
        output.WriteLine($"Reservation setup: {tables.Length} eligible tables, {overlapConflicts} overlap conflicts.");
        if (string.IsNullOrWhiteSpace(ReservationId))
            throw new InvalidOperationException($"No free four-person table was found across {candidateDays.Length} distinct future dates and {tables.Length} tables; {overlapConflicts} overlap conflicts.");
        output.WriteLine($"AQA run {RunId}: reservation {ReservationId}");

        var token = await Api.GetAsync<InviteToken>($"reservations/{ReservationId}/invite-token", OwnerToken);
        InviteToken = token.Token;
        GroupId = (await Api.GetAsync<GroupInvite>($"guest/invites/{Uri.EscapeDataString(InviteToken)}")).GroupReservationId;
    }

    public async Task<GuestIdentity> JoinAsync(string label)
    {
        var guest = await Api.JoinAsync(GroupId, $"Aqa {label} {RunId}");
        guestIds.Add(guest.SessionId);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        do
        {
            using var response = await Api.SendAsync(HttpMethod.Get, $"guest/sessions/{guest.SessionId}", guest.SessionToken);
            if (response.StatusCode == HttpStatusCode.OK) return guest;
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                GroupApi.RequireStatus(response, HttpStatusCode.OK, "Wait for guest session visibility");
            // The token index is eventually consistent immediately after joining.
            await Task.Delay(300);
        } while (DateTime.UtcNow < deadline);
        throw new InvalidOperationException("Guest token did not become visible within 15 seconds.");
    }

    public async Task<ParticipantsSnapshot> WaitForSnapshotAsync(Func<ParticipantsSnapshot, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        ParticipantsSnapshot snapshot;
        do
        {
            snapshot = await Api.ParticipantsAsync(GroupId, OwnerToken);
            if (predicate(snapshot)) return snapshot;
            await Task.Delay(300);
        } while (DateTime.UtcNow < deadline);
        throw new InvalidOperationException("Participant snapshot did not reach the expected state within 15 seconds.");
    }

    public async ValueTask DisposeAsync()
    {
        var errors = new List<string>();
        try
        {
            foreach (var id in guestIds)
            {
                try
                {
                    using var response = await Api.SendAsync(HttpMethod.Delete, $"guest/groups/{GroupId}/sessions/{id}", OwnerToken);
                    if (response.StatusCode != HttpStatusCode.NotFound)
                        GroupApi.RequireStatus(response, HttpStatusCode.OK, "Remove test guest during cleanup");
                }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
            if (!string.IsNullOrEmpty(ReservationId))
            {
                try
                {
                    using var response = await Api.SendAsync(HttpMethod.Delete, "reservations", OwnerToken, new { id = ReservationId });
                    GroupApi.RequireStatus(response, HttpStatusCode.OK, "Cancel test reservation during cleanup");
                }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
        }
        finally { Api.Dispose(); }
        if (errors.Count > 0) throw new InvalidOperationException("AQA cleanup failed: " + string.Join("; ", errors));
    }
}
