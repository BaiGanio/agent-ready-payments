using System.Net;
using Xunit;

namespace Payments.Api.Tests;

/// <summary>Proves the harness, not the API: a container, a migrated schema and a live app.</summary>
public sealed class HealthTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Health_is_ok()
    {
        var response = await Client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
