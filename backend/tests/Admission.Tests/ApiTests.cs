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
        Environment.SetEnvironmentVariable("ADMIN_REGISTRATION_KEY", "local-school-admin-key");
    }

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly HttpClient _client;
    private Task? _adminSetup;

    public ApiTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<string> NewClass(int seats)
    {
        await EnsureAdmin();
        var response = await _client.PostAsJsonAsync(
            "/classes",
            new { name = $"Class 6 Morning {Guid.NewGuid():N}", totalSeats = seats });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClassDto>())!.Id;
    }

    private Task EnsureAdmin() => _adminSetup ??= RegisterAdmin();

    private async Task RegisterAdmin()
    {
        var username = $"test{Guid.NewGuid():N}";
        using var response = await _client.PostAsJsonAsync("/admin/register", new
        {
            userName = username,
            password = "test-password-12345",
            registrationKey = "local-school-admin-key"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static HttpRequestMessage Hold(string classId, string studentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        request.Headers.Add("X-Student-Id", studentId);
        return request;
    }

    private static HttpRequestMessage Confirm(string holdId, string studentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
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
    public async Task MalformedJsonReturnsBadRequestInsteadOfServerError()
    {
        using var content = new StringContent("{", System.Text.Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("/classes", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidClassDetailsReturnBadRequest()
    {
        await EnsureAdmin();
        using var response = await _client.PostAsJsonAsync(
            "/classes",
            new { name = "  ", totalSeats = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminRegistrationLoginAndLogoutManageSession()
    {
        var username = $"admin{Guid.NewGuid():N}";
        const string password = "strong-admin-password-123";
        using var register = await _client.PostAsJsonAsync("/admin/register", new
        {
            userName = username,
            password,
            registrationKey = "local-school-admin-key"
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/admin/me")).StatusCode);

        using var logout = await _client.PostAsync("/admin/logout", null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/admin/me")).StatusCode);

        using var login = await _client.PostAsJsonAsync("/admin/login", new { userName = username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/admin/me")).StatusCode);
    }

    [Fact]
    public async Task ClassCreationRequiresAnAdminSession()
    {
        using var response = await _client.PostAsJsonAsync("/classes", new { name = "Class 6 – Morning Shift", totalSeats = 10 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminCannotLowerCapacityBelowHeldSeats()
    {
        var classId = await NewClass(2);
        using var first = Hold(classId, "20152691234567895");
        using var second = Hold(classId, "20152691234567896");
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(second)).StatusCode);

        using var response = await _client.PutAsJsonAsync($"/classes/{classId}/capacity", new { totalSeats = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AdminCanSeeConfirmedStudentRegistrationNumbers()
    {
        const string studentId = "20152691234567897";
        var classId = await NewClass(1);
        using var holdRequest = Hold(classId, studentId);
        using var holdResponse = await _client.SendAsync(holdRequest);
        Assert.Equal(HttpStatusCode.Created, holdResponse.StatusCode);
        var hold = (await holdResponse.Content.ReadFromJsonAsync<HoldDto>())!;
        using var confirmRequest = Confirm(hold.HoldId, studentId);
        using var confirmResponse = await _client.SendAsync(confirmRequest);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var students = await _client.GetFromJsonAsync<List<AdminStudentDto>>($"/admin/classes/{classId}/students");

        var admittedStudent = Assert.Single(students!);
        Assert.Equal(studentId, admittedStudent.StudentId);
    }

    [Fact]
    public async Task ConfirmingTheSameHoldTwiceConsumesOnlyOneSeat()
    {
        const string studentId = "20152691234567891";
        var classId = await NewClass(1);
        using var holdRequest = Hold(classId, studentId);
        using var holdResponse = await _client.SendAsync(holdRequest);
        Assert.Equal(HttpStatusCode.Created, holdResponse.StatusCode);
        var hold = (await holdResponse.Content.ReadFromJsonAsync<HoldDto>())!;

        using var firstRequest = Confirm(hold.HoldId, studentId);
        using var secondRequest = Confirm(hold.HoldId, studentId);
        using var first = await _client.SendAsync(firstRequest);
        using var second = await _client.SendAsync(secondRequest);
        var seats = await _client.GetFromJsonAsync<Seats>($"/classes/{classId}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, seats!.ConfirmedSeats);
        Assert.Equal(0, seats.AvailableSeats);
    }

    [Fact]
    public async Task AnotherStudentCannotConfirmTheHold()
    {
        var classId = await NewClass(1);
        using var holdRequest = Hold(classId, "20152691234567892");
        using var holdResponse = await _client.SendAsync(holdRequest);
        var hold = (await holdResponse.Content.ReadFromJsonAsync<HoldDto>())!;

        using var response = await _client.SendAsync(Confirm(hold.HoldId, "20152691234567893"));
        var seats = await _client.GetFromJsonAsync<Seats>($"/classes/{classId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, seats!.HeldSeats);
        Assert.Equal(0, seats.ConfirmedSeats);
    }

    [Fact]
    public async Task CancellingHoldReleasesTheSeat()
    {
        const string studentId = "20152691234567894";
        var classId = await NewClass(1);
        using var holdRequest = Hold(classId, studentId);
        using var holdResponse = await _client.SendAsync(holdRequest);
        var hold = (await holdResponse.Content.ReadFromJsonAsync<HoldDto>())!;

        using var cancel = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{hold.HoldId}");
        cancel.Headers.Add("X-Student-Id", studentId);
        using var response = await _client.SendAsync(cancel);
        var seats = await _client.GetFromJsonAsync<Seats>($"/classes/{classId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, seats!.HeldSeats + seats.ConfirmedSeats);
        Assert.Equal(1, seats.AvailableSeats);
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
    private sealed record HoldDto(string HoldId);
    private sealed record AdminStudentDto(string HoldId, string StudentId, string StudentName, DateTime? ConfirmedAt);
    private sealed record Seats(string Id, string Name, int TotalSeats, int HeldSeats, int ConfirmedSeats, int AvailableSeats);
}
