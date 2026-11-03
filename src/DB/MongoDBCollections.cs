using MongoDB.Driver;

namespace ui.DB;

public sealed class MongoDBCollections(IMongoDatabase db)
{
    public IMongoCollection<MongoDBUISession> Sessions => db.GetCollection<MongoDBUISession>("sessions");
}
