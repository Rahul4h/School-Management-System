using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
public sealed class ApiTests:IDisposable {
 static ApiTests(){Environment.SetEnvironmentVariable("DATABASE_URL",Environment.GetEnvironmentVariable("DATABASE_URL")??"Host=db;Port=5432;Database=admission;Username=admission;Password=admission");}
 private readonly WebApplicationFactory<Program> _factory=new(); private readonly HttpClient _client;
 public ApiTests(){_client=_factory.CreateClient();}
 public void Dispose(){_client.Dispose();_factory.Dispose();}
 private async Task<string> NewClass(int seats){var r=await _client.PostAsJsonAsync("/classes",new{name="Class 6 Morning",totalSeats=seats});Assert.Equal(HttpStatusCode.Created,r.StatusCode);return(await r.Content.ReadFromJsonAsync<ClassDto>())!.Id;}
 private HttpRequestMessage Hold(string cid,string student){var r=new HttpRequestMessage(HttpMethod.Post,$"/classes/{cid}/holds");r.Headers.Add("X-Student-Id",student);return r;}
 [Fact]public async Task NewClassStartsWithAllSeatsAvailable(){var id=await NewClass(4);var c=await _client.GetFromJsonAsync<Seats>($"/classes/{id}");Assert.Equal(4,c!.TotalSeats);Assert.Equal(4,c.AvailableSeats);}
 [Fact]public async Task OneStudentCannotHoldTwoSeatsInOneClass(){var id=await NewClass(3);var student="20152691234567890";Assert.Equal(HttpStatusCode.Created,(await _client.SendAsync(Hold(id,student))).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await _client.SendAsync(Hold(id,student))).StatusCode);}
 [Fact]public async Task ConcurrentHoldsCannotExceedCapacity(){var id=await NewClass(5);var jobs=Enumerable.Range(0,30).Select(i=>_client.SendAsync(Hold(id,(10000000000000000L+i).ToString())));var responses=await Task.WhenAll(jobs);Assert.Equal(5,responses.Count(x=>x.StatusCode==HttpStatusCode.Created));Assert.Equal(25,responses.Count(x=>x.StatusCode==HttpStatusCode.Conflict));var c=await _client.GetFromJsonAsync<Seats>($"/classes/{id}");Assert.Equal(0,c!.AvailableSeats);}
 private sealed record ClassDto(string Id,string Name,int TotalSeats);private sealed record Seats(string Id,string Name,int TotalSeats,int HeldSeats,int ConfirmedSeats,int AvailableSeats);
}
