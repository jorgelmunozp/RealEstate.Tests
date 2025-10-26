using System;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace RealEstate.Tests
{
    [TestFixture]
    public class MongoConnectionTests
    {
        [Test]
        [Category("Integration")]
        public async Task Ping_ShouldSucceed_WhenMongoIsReachable()
        {
            // Para habilitar esta prueba, establecer RUN_MONGO_TESTS=1
            var run = Environment.GetEnvironmentVariable("RUN_MONGO_TESTS");
            if (!string.Equals(run, "1", StringComparison.Ordinal))
            {
                Assert.Inconclusive("RUN_MONGO_TESTS!=1. Omite prueba de conexión a Mongo.");
            }

            var connection = Environment.GetEnvironmentVariable("MONGO_CONNECTION") ?? "mongodb://localhost:27017";
            var databaseName = Environment.GetEnvironmentVariable("MONGO_DATABASE") ?? "admin";

            var settings = MongoClientSettings.FromConnectionString(connection);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
            settings.ConnectTimeout = TimeSpan.FromSeconds(2);

            var client = new MongoClient(settings);
            var db = client.GetDatabase(databaseName);

            var result = await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
            result.Should().NotBeNull();
            result.GetValue("ok", defaultValue: 0).ToDouble().Should().Be(1.0);
        }
    }
}

