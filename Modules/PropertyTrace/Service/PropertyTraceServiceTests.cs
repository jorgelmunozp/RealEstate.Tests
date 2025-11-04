using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using RealEstate.API.Modules.PropertyTrace.Dto;
using RealEstate.API.Modules.PropertyTrace.Model;
using RealEstate.API.Modules.PropertyTrace.Service;
using AutoMapper;
using System.Linq.Expressions;

namespace RealEstate.API.Tests
{
    [TestFixture]
    public class PropertyTraceServiceTests
    {
        // ---- Helper para evitar DeleteResult.Acknowledged(...) ----
        private sealed class FakeDeleteResult : DeleteResult
        {
            private readonly long _deleted;
            public FakeDeleteResult(long deleted) { _deleted = deleted; }
            public override bool IsAcknowledged => true;
            public override long DeletedCount => _deleted;
        }

        private Mock<IMongoDatabase> _db = null!;
        private Mock<IMongoCollection<PropertyTraceModel>> _colTraces = null!;
        private IMemoryCache _cache = null!;
        private IConfiguration _config = null!;
        private Mock<IValidator<PropertyTraceDto>> _validatorMock = null!;
        private IValidator<PropertyTraceDto> _validator = null!;
        private IMapper _mapper = null!;

        [SetUp]
        public void Setup()
        {
            _config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MONGO_COLLECTION_PROPERTYTRACE"] = "traces",
                    ["CACHE_TTL_MINUTES"] = "5",
                })
                .Build();

            _cache = new MemoryCache(new MemoryCacheOptions());

            // Validator por defecto => válido
            _validatorMock = new Mock<IValidator<PropertyTraceDto>>(MockBehavior.Strict);
            _validatorMock
                .Setup(v => v.ValidateAsync(It.IsAny<PropertyTraceDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _validator = _validatorMock.Object;

            // Mapper dummy (no mapeamos nada en estos tests)
            _mapper = new Mock<IMapper>(MockBehavior.Loose).Object;

            _db = new Mock<IMongoDatabase>(MockBehavior.Strict);
            _colTraces = new Mock<IMongoCollection<PropertyTraceModel>>(MockBehavior.Strict);

            _db.Setup(d => d.GetCollection<PropertyTraceModel>("traces", It.IsAny<MongoCollectionSettings>()))
               .Returns(_colTraces.Object);
        }

        private PropertyTraceService CreateSut() =>
            new PropertyTraceService(_db.Object, _validator, _config, _cache, _mapper);

        [Test]
        public async Task GetAllAsync_cache_hit_all()
        {
            // Arrange (cache “all”)
            var cacheKey = "ptrace:all";
            var cached = new List<PropertyTraceDto>
            {
                new PropertyTraceDto { IdPropertyTrace = "t1", IdProperty = "p1", Name = "Venta" }
            };
            _cache.Set(cacheKey, cached, TimeSpan.FromMinutes(5));

            var sut = CreateSut();

            // Act
            var res = await sut.GetAllAsync(idProperty: null, refresh: false);

            // Assert
            res.Success.Should().BeTrue();
            res.Message.Should().Contain("caché");
            res.Data.Should().BeSameAs(cached);
        }

        [Test]
        public async Task GetAllAsync_cache_hit_porPropiedad()
        {
            // Arrange (cache para propiedad específica)
            var idProp = "p123";
            var cacheKey = $"ptrace:{idProp}";
            var cached = new List<PropertyTraceDto>
            {
                new PropertyTraceDto { IdPropertyTrace = "t2", IdProperty = idProp, Name = "Arriendo" }
            };
            _cache.Set(cacheKey, cached, TimeSpan.FromMinutes(5));

            var sut = CreateSut();

            // Act
            var res = await sut.GetAllAsync(idProperty: idProp, refresh: false);

            // Assert
            res.Success.Should().BeTrue();
            res.Message.Should().Contain("caché");
            res.Data.Should().BeSameAs(cached);
        }

        [Test]
        public async Task CreateSingleAsync_validacionInvalida_noInserta_y_400()
        {
            // Validator inválido
            var invalid = new ValidationResult(new[] { new ValidationFailure("Name", "Requerido") });
            _validatorMock
                .Setup(v => v.ValidateAsync(It.IsAny<PropertyTraceDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(invalid);

            var sut = CreateSut();

            var dto = new PropertyTraceDto
            {
                IdProperty = "p1",
                Name = "", // fuerza invalidez
                Value = 1000,
                Tax = 5,
                DateSale = "2020-01-01"
            };

            // No configuramos InsertOneAsync; si se llama, Moq Strict fallará
            var res = await sut.CreateSingleAsync(dto);

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
            _colTraces.Verify(c =>
                c.InsertOneAsync(It.IsAny<PropertyTraceModel>(), null, It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task DeleteAsync_notFound_404()
        {
            // DeleteOneAsync(Expression, CancellationToken) => 0 borrados
            _colTraces
                .Setup(c => c.DeleteOneAsync(
                    It.IsAny<Expression<Func<PropertyTraceModel, bool>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<DeleteResult>(new FakeDeleteResult(0)));

            var sut = CreateSut();

            var res = await sut.DeleteAsync("no-existe");

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task DeleteAsync_ok_devuelve_deleted()
        {
            _colTraces
                .Setup(c => c.DeleteOneAsync(
                    It.IsAny<Expression<Func<PropertyTraceModel, bool>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<DeleteResult>(new FakeDeleteResult(1)));

            var sut = CreateSut();

            var res = await sut.DeleteAsync("t1");

            res.Success.Should().BeTrue();
            res.Message.Should().Contain("eliminada");
        }
    }
}
