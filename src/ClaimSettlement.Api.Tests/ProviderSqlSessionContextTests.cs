using ClaimSettlement.Infrastructure.Persistence;
using Xunit;

namespace ClaimSettlement.Api.Tests;

public sealed class ProviderSqlSessionContextTests
{
    [Fact]
    public void BeginSetsProviderAndRestoresPreviousProvider()
    {
        var sessionContext = new ProviderSqlSessionContext();

        using (sessionContext.Begin("provider-1"))
        {
            Assert.Equal("provider-1", sessionContext.ProviderId);

            using (sessionContext.Begin("provider-2"))
            {
                Assert.Equal("provider-2", sessionContext.ProviderId);
            }

            Assert.Equal("provider-1", sessionContext.ProviderId);
        }

        Assert.Null(sessionContext.ProviderId);
    }

    [Fact]
    public void BeginWithEmptyProviderClearsExistingProvider()
    {
        var sessionContext = new ProviderSqlSessionContext();

        using (sessionContext.Begin("provider-1"))
        {
            using (sessionContext.Begin(null))
            {
                Assert.Null(sessionContext.ProviderId);
            }

            Assert.Equal("provider-1", sessionContext.ProviderId);
        }
    }
}