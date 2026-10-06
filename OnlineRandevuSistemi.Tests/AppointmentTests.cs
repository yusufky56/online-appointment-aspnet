using System.Net;
using static System.Net.WebUtility;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineRandevuSistemi.Data;
using OnlineRandevuSistemi.Models;

namespace OnlineRandevuSistemi.Tests;

public class AppointmentTests : IClassFixture<AppFactory>, IAsyncLifetime
{
    private readonly AppFactory _factory;

    public AppointmentTests(AppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _ = _factory.Server; // starts the app, which runs migrations and seeding
        await _factory.SeedUsersAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Appointments.RemoveRange(db.Appointments);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient ClientFor(string? userId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (userId != null)
        {
            client.DefaultRequestHeaders.Add("X-Test-User", userId);
        }
        return client;
    }

    private static async Task<string> AntiforgeryTokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    }

    private static DateTime NextWeekdayAt(int hour)
    {
        var day = DateTime.Today.AddDays(1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) day = day.AddDays(1);
        return day.AddHours(hour);
    }

    private async Task<int> AddAppointmentAsync(string userId, DateTime at, AppointmentStatus status = AppointmentStatus.Pending)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var appt = new Appointment { UserId = userId, AppointmentDate = at, ServiceType = "Saç Kesimi", Status = status };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();
        return appt.Id;
    }

    private async Task<Appointment?> FindAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
    }

    private async Task<HttpResponseMessage> PostCreateAsync(HttpClient client, DateTime at, string service = "Masaj")
    {
        var token = await AntiforgeryTokenAsync(client, "/Appointment/Create");
        return await client.PostAsync("/Appointment/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["AppointmentDate"] = at.ToString("yyyy-MM-ddTHH:mm"),
            ["ServiceType"] = service,
            ["Notes"] = "",
        }));
    }

    [Fact]
    public async Task Anonymous_user_is_redirected_to_login()
    {
        var res = await ClientFor(null).GetAsync("/Appointment");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
    }

    [Fact]
    public async Task Home_page_loads()
    {
        var res = await ClientFor(null).GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task User_can_create_an_appointment()
    {
        var client = ClientFor(AppFactory.Alice);
        var res = await PostCreateAsync(client, NextWeekdayAt(10));

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var list = await client.GetStringAsync("/Appointment");
        Assert.Contains("Masaj", list);
    }

    [Fact]
    public async Task Appointment_in_the_past_is_rejected()
    {
        var res = await PostCreateAsync(ClientFor(AppFactory.Alice), DateTime.Today.AddDays(-1).AddHours(10));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode); // form shown again
        Assert.Contains("Geçmiş bir tarihe randevu alınamaz", HtmlDecode(await res.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Appointment_outside_working_hours_is_rejected()
    {
        var res = await PostCreateAsync(ClientFor(AppFactory.Alice), NextWeekdayAt(20));
        Assert.Contains("09:00 - 18:00", HtmlDecode(await res.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Overlapping_slot_is_rejected()
    {
        await AddAppointmentAsync(AppFactory.Bob, NextWeekdayAt(11));
        var res = await PostCreateAsync(ClientFor(AppFactory.Alice), NextWeekdayAt(11).AddMinutes(30));
        Assert.Contains("Bu saat dolu", HtmlDecode(await res.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task User_can_cancel_own_appointment()
    {
        var id = await AddAppointmentAsync(AppFactory.Alice, NextWeekdayAt(12));
        var client = ClientFor(AppFactory.Alice);
        var token = await AntiforgeryTokenAsync(client, "/Appointment");

        var res = await client.PostAsync("/Appointment/Cancel", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = id.ToString(),
        }));

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(AppointmentStatus.Cancelled, (await FindAsync(id))!.Status);
    }

    [Fact]
    public async Task User_cannot_cancel_someone_elses_appointment()
    {
        await AddAppointmentAsync(AppFactory.Alice, NextWeekdayAt(9)); // so Alice's page has a form token
        var bobsId = await AddAppointmentAsync(AppFactory.Bob, NextWeekdayAt(14));
        var client = ClientFor(AppFactory.Alice);
        var token = await AntiforgeryTokenAsync(client, "/Appointment");

        var res = await client.PostAsync("/Appointment/Cancel", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = bobsId.ToString(),
        }));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(AppointmentStatus.Pending, (await FindAsync(bobsId))!.Status);
    }

    [Fact]
    public async Task Calendar_feed_only_contains_own_appointments()
    {
        await AddAppointmentAsync(AppFactory.Alice, NextWeekdayAt(9));
        await AddAppointmentAsync(AppFactory.Bob, NextWeekdayAt(15));
        var start = DateTime.Today.ToString("yyyy-MM-dd");
        var end = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd");

        var json = await ClientFor(AppFactory.Alice).GetStringAsync($"/Appointment/GetAppointments?start={start}&end={end}");

        Assert.Single(Regex.Matches(json, "\"id\""));
    }

    [Fact]
    public async Task Admin_panel_requires_admin_role()
    {
        var asUser = await ClientFor(AppFactory.Alice).GetAsync("/Admin");
        Assert.NotEqual(HttpStatusCode.OK, asUser.StatusCode);

        var asAdmin = await ClientFor(AppFactory.Admin).GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
    }
}
