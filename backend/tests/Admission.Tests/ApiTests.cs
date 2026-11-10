using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public sealed class ApiTests : IDisposable
{
    static ApiTests()
    {
        Environment.SetEnvironmentVariable(
            "DATABASE_URL",
            Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? "Host=db;Port=5432;Database=admission;Username=admission;Password=admission");
    }

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly HttpClient _client;

    public ApiTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<string> NewClass(int seats)
    {
        var response = await _client.PostAsJsonAsync(
            "/classes",
            new { name = $"Class 6 Morning {Guid.NewGuid():N}", totalSeats = seats });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClassDto>())!.Id;
    }

    private static HttpRequestMessage Hold(string classId, string studentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        request.Headers.Add("X-Student-Id", studentId);
        return request;
    }

    [Fact]
    public async Task NewClassStartsWithAllSeatsAvailable()
    {
        var classId = await NewClass(4);
        var result = await _client.GetFromJsonAsync<Seats>($"/classes/{classId}");

        Assert.Equal(4, result!.TotalSeats);
        Assert.Equal(4, result.AvailableSeats);
        Assert.Equal(0, result.HeldSeats);
        Assert.Equal(0, result.ConfirmedSeats);
    }

    [Fact]
    public async Task OneStudentCannotHoldTwoSeatsInOneClass()
    {
        var classId = await NewClass(3);
        const string studentId = "20152691234567890";

        using var first = Hold(classId, studentId);
        using var duplicate = Hold(classId, studentId);
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync(duplicate)).StatusCode);
    }

    [Fact]
    public async Task NonAsciiRegistrationDigitsAreRejected()
    {
        // Bengali digits look numeric but the contract and database require ASCII 0-9.
        using var request = Hold("c_not_needed", string.Concat(Enumerable.Repeat("\u09E8", 17)));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentApplicationsNeverHoldMoreThanTheSeatCapacity()
    {
        const int seatCapacity = 5;
        const int simultaneousApplicants = 30;
        var classId = await NewClass(seatCapacity);

        // SendAsync starts all requests before Task.WhenAll waits for their responses.
        var requests = Enumerable.Range(0, simultaneousApplicants)
            .Select(index => _client.SendAsync(Hold(classId, (10000000000000000L + index).ToString())));
        using var responses = new ResponseCollection(await Task.WhenAll(requests));

        var created = responses.Items.Count(response => response.StatusCode == HttpStatusCode.Created);
        var conflicts = responses.Items.Count(response => response.StatusCode == HttpStatusCode.Conflict);
        var seats = await _client.GetFromJsonAsync<Seats>($"/classes/{classId}");

        Assert.Equal(seatCapacity, created);
        Assert.Equal(simultaneousApplicants - seatCapacity, conflicts);
        Assert.Equal(0, seats!.AvailableSeats);
        Assert.Equal(seatCapacity, seats.HeldSeats + seats.ConfirmedSeats);
    }

    private sealed class ResponseCollection(HttpResponseMessage[] items) : IDisposable
    {
        public HttpResponseMessage[] Items { get; } = items;
        public void Dispose()
        {
            foreach (var item in Items) item.Dispose();
        }
    }

    private sealed record ClassDto(string Id, string Name, int TotalSeats);
    private sealed record Seats(string Id, string Name, int TotalSeats, int HeldSeats, int ConfirmedSeats, int AvailableSeats);
}