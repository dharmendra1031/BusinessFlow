using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class ResmohWebsiteTests
{
    private const string Secret = "Resmoh_Public_Intake_Test_Secret_2026_123456";
    private const string Root = "https://resmoh.in";

    private static object Enquiry(string phone = "+91 98765 43210", string website = "") => new
    {
        submissionKey = "012345678901234567890123",
        service = "ERP / CRM",
        name = "Sample Website Visitor",
        phone,
        email = "visitor@example.test",
        company = "Sample Organization",
        country = "India",
        budget = "Need an estimate",
        timeline = "1-3 months",
        details = "Need a custom project with inventory, purchase, sales and accounts.",
        website,
        consentAccepted = true
    };

    private static WebApplicationFactory<Resmoh.Web.Program> Site(
        RecordingCrm crm, bool configured = true)
    {
        var websiteRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../public-site/Resmoh.Web"));
        return new WebApplicationFactory<Resmoh.Web.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(websiteRoot);
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AllowedHosts"] = "*",
                    ["PublicSite:Origin"] = Root,
                    ["BusinessFlow:IntakeUrl"] = configured
                        ? "https://businessflow.test/api/integrations/website/leads" : "",
                    ["BusinessFlow:SigningSecret"] = configured ? Secret : ""
                }));
            builder.ConfigureTestServices(services =>
                services.AddHttpClient("businessflow")
                    .ConfigurePrimaryHttpMessageHandler(() => crm));
        });
    }

    [Fact]
    public async Task Landing_and_requirement_form_have_no_crm_secret()
    {
        using var crm = new RecordingCrm();
        await using var site = Site(crm);
        using var client = site.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri(Root) });
        var home = await client.GetStringAsync("/");
        var form = await client.GetStringAsync("/start-project/");
        var scripts = await client.GetStringAsync("/site.js");

        Assert.Contains("Resmoh IT", home);
        Assert.Contains("Start a project", home, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Project request", form, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/project-requests", scripts);
        Assert.DoesNotContain(Secret, home + form + scripts);
        Assert.DoesNotContain("X-Website-Signature", scripts);
    }

    [Fact]
    public async Task Project_enquiry_forwards_validated_payload_with_matching_hmac()
    {
        using var crm = new RecordingCrm();
        await using var site = Site(crm);
        using var client = site.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri(Root) });
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", Root);

        var response = await client.PostAsJsonAsync("/api/project-requests", Enquiry());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, crm.Requests);
        var lead = JsonSerializer.Deserialize<JsonElement>(crm.Body!);
        Assert.Equal("resmoh-012345678901234567890123",
            lead.GetProperty("eventId").GetString());
        Assert.Equal("Sample Website Visitor", lead.GetProperty("name").GetString());
        Assert.StartsWith("Service: ERP / CRM", lead.GetProperty("requirement").GetString());
        Assert.Contains("purchase, sales", lead.GetProperty("requirement").GetString());
        Assert.True(lead.GetProperty("consentAccepted").GetBoolean());

        var timestamp = crm.Timestamp!;
        Assert.InRange(long.Parse(timestamp),
            DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(),
            DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds());
        var bytes = Encoding.UTF8.GetBytes(timestamp + ".")
            .Concat(Encoding.UTF8.GetBytes(crm.Body!)).ToArray();
        var expected = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), bytes));
        Assert.Equal(expected, crm.Signature);
    }

    [Fact]
    public async Task Invalid_input_origin_spam_or_missing_config_never_reaches_crm()
    {
        using var crm = new RecordingCrm();
        await using var site = Site(crm);
        using var client = site.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri(Root) });

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/project-requests", Enquiry())).StatusCode);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", Root);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/project-requests", Enquiry(phone: "123"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/project-requests", Enquiry(website: "spam.example"))).StatusCode);
        Assert.Equal(0, crm.Requests);

        using var disabledCrm = new RecordingCrm();
        await using var disabledSite = Site(disabledCrm, configured: false);
        using var disabledClient = disabledSite.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri(Root) });
        disabledClient.DefaultRequestHeaders.TryAddWithoutValidation("Origin", Root);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await disabledClient.PostAsJsonAsync("/api/project-requests", Enquiry())).StatusCode);
        Assert.Equal(0, disabledCrm.Requests);
    }

    private sealed class RecordingCrm : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public string? Body { get; private set; }
        public string? Timestamp { get; private set; }
        public string? Signature { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Timestamp = request.Headers.GetValues("X-Website-Timestamp").Single();
            Signature = request.Headers.GetValues("X-Website-Signature").Single();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://businessflow.test/api/integrations/website/leads",
                request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}
