using Kor.Inspections.App.Services;

namespace Kor.Inspections.Tests.Services;

public class GraphTokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_ReusesCachedToken_WhenStillValid()
    {
        var source = new SpyGraphAccessTokenSource(
            new GraphAccessToken("token-1", DateTimeOffset.UtcNow.AddHours(1)));
        var provider = new GraphTokenProvider(source);

        var first = await provider.GetTokenAsync();
        var second = await provider.GetTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal("token-1", second);
        Assert.Equal(1, source.CallCount);
    }

    private sealed class SpyGraphAccessTokenSource : IGraphAccessTokenSource
    {
        private readonly GraphAccessToken _token;

        public SpyGraphAccessTokenSource(GraphAccessToken token)
        {
            _token = token;
        }

        public int CallCount { get; private set; }

        public Task<GraphAccessToken> AcquireTokenAsync()
        {
            CallCount++;
            return Task.FromResult(_token);
        }
    }
}
