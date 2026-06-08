using System.Text.Json;
using Application.SocialMedias;
using Domain.Entities;
using FluentAssertions;

namespace test;

public sealed class SocialMediaExternalAccountKeyTests
{
    [Fact]
    public void Resolve_ShouldReturnSameKeyForDifferentUsersConnectedToSameExternalAccount()
    {
        var firstConnection = CreateSocialMedia(Guid.NewGuid(), Guid.NewGuid(), "shared-page");
        var secondConnection = CreateSocialMedia(Guid.NewGuid(), Guid.NewGuid(), "shared-page");

        SocialMediaExternalAccountKey.Resolve(firstConnection)
            .Should()
            .Be(SocialMediaExternalAccountKey.Resolve(secondConnection))
            .And.Be("facebook:account:shared-page");
    }

    private static SocialMedia CreateSocialMedia(Guid id, Guid userId, string pageId) =>
        new()
        {
            Id = id,
            UserId = userId,
            Type = "facebook",
            Metadata = JsonDocument.Parse($$"""{"page_id":"{{pageId}}"}""")
        };
}
