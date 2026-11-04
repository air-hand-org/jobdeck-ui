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
    [property: BsonGuidRepresentation(GuidRepresentation.Standard), BsonId]
    Guid SessionId,
    string AccessToken,
    DateTime ExpiresAt,
    MongoDBUIUserProfile Profile
)
{
    public UISession ToDomain() =>
        new(
            SessionId: SessionId,
            AccessToken: AccessToken,
            AccessTokenExpiresAt: ExpiresAt,
            Profile: Profile.ToDomain()
        );

    public static MongoDBUISession FromDomain(UISession session) =>
        new(
            SessionId: session.SessionId,
            AccessToken: session.AccessToken,
            ExpiresAt: session.AccessTokenExpiresAt.DateTime,
            Profile: MongoDBUIUserProfile.FromDomain(session.Profile)
        );
}
