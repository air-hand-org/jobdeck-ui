using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ui.Session;

namespace ui.DB;

public sealed record MongoDBUIUserProfile(string Name)
{
    public UIUserProfile ToDomain() => new(Name);
    public static MongoDBUIUserProfile FromDomain(UIUserProfile profile) => new(profile.Name);
}

public sealed record MongoDBUISession(
    [property: BsonGuidRepresentation(GuidRepresentation.Standard)]
    Guid SessionId,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    MongoDBUIUserProfile Profile
)
{
    public UISession ToDomain() => new(SessionId, AccessToken, ExpiresAt, Profile.ToDomain());

    public static MongoDBUISession FromDomain(UISession session) =>
        new(session.SessionId, session.AccessToken, session.AccessTokenExpiresAt, MongoDBUIUserProfile.FromDomain(session.Profile));
}
