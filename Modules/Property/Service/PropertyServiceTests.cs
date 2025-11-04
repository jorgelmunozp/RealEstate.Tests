using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using RealEstate.API.Modules.Property.Dto;
using RealEstate.API.Modules.Property.Service;
using RealEstate.API.Modules.Owner.Interface;
using RealEstate.API.Modules.PropertyImage.Interface;
using RealEstate.API.Modules.PropertyTrace.Interface;
using RealEstate.API.Modules.Property.Model;
using RealEstate.API.Modules.Owner.Model;
using RealEstate.API.Modules.PropertyImage.Model;
using RealEstate.API.Modules.PropertyTrace.Model;

namespace RealEstate.Tests.Modules.Property.Service
{
    [TestFixture]
    public class PropertyServiceTest
    {
        private IMemoryCache _cache = null!;
        private IConfiguration _config = null!;
        private IValidator<PropertyDto> _validator = null!;
        private IOwnerService _ownerService = null!;
        private IPropertyImageService _imageService = null!;
        private IPropertyTraceService _traceService = null!;
        private Mock<IMongoDatabase> _db = null!;

        [SetUp]
        public void Setup()
        {
            // Config simple
            _config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MONGO_COLLECTION_PROPERTY"] = "properties",
                    ["MONGO_COLLECTION_OWNER"] = "owners",
                    ["MONGO_COLLECTION_PROPERTYIMAGE"] = "images",
                    ["MONGO_COLLECTION_PROPERTYTRACE"] = "traces",
                    ["CACHE_TTL_MINUTES"] = "5",
                })
                .Build();

            _cache = new MemoryCache(new MemoryCacheOptions());

            // Validator dummy
            var val = new Mock<IValidator<PropertyDto>>();
            val.Setup(v => v.ValidateAsync(It.IsAny<PropertyDto>(), default))
               .ReturnsAsync(new ValidationResult());
            _validator = val.Object;

            // Servicios secundarios dummy
            _ownerService = Mock.Of<IOwnerService>();
            _imageService = Mock.Of<IPropertyImageService>();
            _traceService = Mock.Of<IPropertyTraceService>();

            // Mongo: solo devolvemos colecciones mockeadas, no configuramos Find para evitar overloads
            _db = new Mock<IMongoDatabase>(MockBehavior.Strict);
            _db.Setup(d => d.GetCollection<PropertyModel>("properties", It.IsAny<MongoCollectionSettings>()))
               .Returns(new Mock<IMongoCollection<PropertyModel>>().Object);
            _db.Setup(d => d.GetCollection<OwnerModel>("owners", It.IsAny<MongoCollectionSettings>()))
               .Returns(new Mock<IMongoCollection<OwnerModel>>().Object);
            _db.Setup(d => d.GetCollection<PropertyImageModel>("images", It.IsAny<MongoCollectionSettings>()))
               .Returns(new Mock<IMongoCollection<PropertyImageModel>>().Object);
            _db.Setup(d => d.GetCollection<PropertyTraceModel>("traces", It.IsAny<MongoCollectionSettings>()))
               .Returns(new Mock<IMongoCollection<PropertyTraceModel>>().Object);
        }

        private PropertyService CreateSut() =>
            new PropertyService(_db.Object, _validator, _config, _cache, _ownerService, _imageService, _traceService);

        [Test]
        public async Task GetCachedAsync_devuelve_desde_cache_sin_tocar_mongo()
        {
            // Arrange
            var page = 1; var limit = 6;
            string? name = null, address = null, idOwner = null;
            long? minPrice = null, maxPrice = null;

            // La misma key que construye el servicio
            var cacheKey = $"{name}-{address}-{idOwner}-{minPrice}-{maxPrice}-{page}-{limit}";

            var cachedPayload = new
            {
                data = new[] { new { idProperty = "p1", name = "Casa" } },
                meta = new { page, limit, total = 1, last_page = 1 }
            };
            _cache.Set(cacheKey, cachedPayload, TimeSpan.FromMinutes(5));

            var sut = CreateSut();

            // Act
            var res = await sut.GetCachedAsync(name, address, idOwner, minPrice, maxPrice, page, limit, refresh: false);

            // Assert
            res.Success.Should().BeTrue();
            res.Message.Should().Contain("caché");
            res.Data.Should().NotBeNull();
        }
    }
}
