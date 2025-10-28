using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Moq;
using MongoDB.Driver;
using NUnit.Framework;
using RealEstate.API.Infraestructure.Core.Services;
using RealEstate.API.Modules.PropertyTrace.Dto;
using RealEstate.API.Modules.PropertyTrace.Model;
using RealEstate.API.Modules.PropertyTrace.Service;

namespace RealEstate.Tests.Modules.PropertyTrace.Service
{
    [TestFixture]
    public class PropertyTraceServiceTests
    {
        private Mock<IMongoDatabase> _db = null!;
        private Mock<IMongoCollection<PropertyTraceModel>> _col = null!;
        private Mock<IMapper> _mapper = null!;
        private IMemoryCache _cache = null!;
        private IConfiguration _config = null!;
        private IValidator<PropertyTraceDto> _validator = null!;
        private PropertyTraceService _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _db = new Mock<IMongoDatabase>();
            _col = new Mock<IMongoCollection<PropertyTraceModel>>();
            _mapper = new Mock<IMapper>();
            _cache = new MemoryCache(new MemoryCacheOptions());

            _validator = Mock.Of<IValidator<PropertyTraceDto>>(v =>
                v.ValidateAsync(It.IsAny<PropertyTraceDto>(), It.IsAny<CancellationToken>()) ==
                Task.FromResult(new ValidationResult()));

            _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MONGO_COLLECTION_PROPERTYTRACE"] = "PropertyTrace",
                ["CACHE_TTL_MINUTES"] = "1"
            }).Build();

            _db.Setup(d => d.GetCollection<PropertyTraceModel>("PropertyTrace", null)).Returns(_col.Object);

            _mapper.Setup(m => m.Map<PropertyTraceDto>(It.IsAny<PropertyTraceModel>()))
                   .Returns<PropertyTraceModel>(m => new PropertyTraceDto { IdProperty = m.IdProperty });

            _mapper.Setup(m => m.Map<PropertyTraceModel>(It.IsAny<PropertyTraceDto>()))
                   .Returns<PropertyTraceDto>(d => new PropertyTraceModel
                   {
                       Id = Guid.NewGuid().ToString("N"),
                       IdProperty = d.IdProperty
                   });

            _mapper.Setup(m => m.Map<IEnumerable<PropertyTraceDto>>(It.IsAny<IEnumerable<PropertyTraceModel>>()))
                   .Returns<IEnumerable<PropertyTraceModel>>(list => list.Select(x => new PropertyTraceDto { IdProperty = x.IdProperty }).ToList());

            _mapper.Setup(m => m.Map(It.IsAny<PropertyTraceDto>(), It.IsAny<PropertyTraceModel>()))
                   .Returns<PropertyTraceDto, PropertyTraceModel>((src, dest) => { dest.IdProperty = src.IdProperty; return dest; });

            _sut = new PropertyTraceService(_db.Object, _validator, _config, _cache, _mapper.Object);
        }

        [TearDown] public void TearDown() => (_cache as MemoryCache)?.Dispose();

        // ------- helpers Mongo -------
        private static Mock<IAsyncCursor<T>> BuildCursor<T>(IEnumerable<T> items)
        {
            var seq = new Queue<IEnumerable<T>>();
            seq.Enqueue(items);
            seq.Enqueue(Enumerable.Empty<T>());

            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
            cursor.Setup(c => c.Current).Returns(() => seq.Peek());
            return cursor;
        }

        private static Mock<IFindFluent<T, T>> BuildFindFluent<T>(IEnumerable<T> items)
        {
            var cursor = BuildCursor(items);
            var find = new Mock<IFindFluent<T, T>>();
            find.Setup(f => f.ToCursor(It.IsAny<CancellationToken>())).Returns(cursor.Object);
            find.Setup(f => f.ToCursorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cursor.Object);
            return find;
        }

        private void SetupFindReturns(IEnumerable<PropertyTraceModel> items)
        {
            var find = BuildFindFluent(items);
            _col.Setup(c => c.Find(It.IsAny<FilterDefinition<PropertyTraceModel>>(), It.IsAny<FindOptions>()))
                .Returns(find.Object);
            _col.Setup(c => c.Find(It.IsAny<Expression<Func<PropertyTraceModel, bool>>>(), It.IsAny<FindOptions>()))
                .Returns(find.Object);
        }

        // ------- tests -------
        [Test]
        public async Task GetAllAsync_NoCache_fetches_maps_and_caches()
        {
            SetupFindReturns(new[]
            {
                new PropertyTraceModel { Id = "t1", IdProperty = "p1" },
                new PropertyTraceModel { Id = "t2", IdProperty = "p2" }
            });

            var res = await _sut.GetAllAsync();

            res.Success.Should().BeTrue();
            res.Data!.Count().Should().Be(2);
            _cache.TryGetValue("ptrace:all", out _).Should().BeTrue();
            _col.Verify(c => c.Find(It.IsAny<FilterDefinition<PropertyTraceModel>>(), It.IsAny<FindOptions>()), Times.Once);
        }

        [Test]
        public async Task GetAllAsync_uses_cache_when_present_and_no_refresh()
        {
            var cached = new[] { new PropertyTraceDto { IdProperty = "PX" } }.AsEnumerable();
            _cache.Set("ptrace:all", cached, TimeSpan.FromMinutes(1));

            var res = await _sut.GetAllAsync();

            res.Success.Should().BeTrue();
            res.Data!.First().IdProperty.Should().Be("PX");
            _col.Verify(c => c.Find(It.IsAny<FilterDefinition<PropertyTraceModel>>(), It.IsAny<FindOptions>()), Times.Never);
        }

        [Test]
        public async Task GetByIdAsync_not_found_returns_404()
        {
            SetupFindReturns(Array.Empty<PropertyTraceModel>());

            var res = await _sut.GetByIdAsync("nope");

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task GetByIdAsync_found_returns_ok_with_dto()
        {
            SetupFindReturns(new[] { new PropertyTraceModel { Id = "t1", IdProperty = "p1" } });

            var res = await _sut.GetByIdAsync("t1");

            res.Success.Should().BeTrue();
            res.Data!.IdProperty.Should().Be("p1");
        }

        [Test]
        public async Task CreateAsync_mixed_valid_invalid_returns_400_and_inserts_only_valid()
        {
            var invalid = new PropertyTraceDto { IdProperty = "p1" };
            var valid = new PropertyTraceDto { IdProperty = "p2" };

            var invalidResult = new ValidationResult(new[] { new ValidationFailure("f", "err") });
            var validResult = new ValidationResult();

            var validatorMock = new Mock<IValidator<PropertyTraceDto>>();
            validatorMock.SetupSequence(v => v.ValidateAsync(It.IsAny<PropertyTraceDto>(), It.IsAny<CancellationToken>()))
                         .ReturnsAsync(invalidResult).ReturnsAsync(validResult);

            _sut = new PropertyTraceService(_db.Object, validatorMock.Object, _config, _cache, _mapper.Object);

            _col.Setup(c => c.InsertOneAsync(It.IsAny<PropertyTraceModel>(), null, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var res = await _sut.CreateAsync(new[] { invalid, valid });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
            _col.Verify(c => c.InsertOneAsync(It.IsAny<PropertyTraceModel>(), null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task CreateSingleAsync_valid_returns_201()
        {
            _col.Setup(c => c.InsertOneAsync(It.IsAny<PropertyTraceModel>(), null, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var dto = new PropertyTraceDto { IdProperty = "p1" };
            var res = await _sut.CreateSingleAsync(dto);

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(201);
            res.Data.Should().NotBeNull();
        }

        [Test]
        public async Task CreateSingleAsync_invalid_returns_400()
        {
            var validatorMock = new Mock<IValidator<PropertyTraceDto>>();
            validatorMock.Setup(v => v.ValidateAsync(It.IsAny<PropertyTraceDto>(), It.IsAny<CancellationToken>()))
                         .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("f", "e") }));
            _sut = new PropertyTraceService(_db.Object, validatorMock.Object, _config, _cache, _mapper.Object);

            var res = await _sut.CreateSingleAsync(new PropertyTraceDto { IdProperty = "p1" });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
            _col.Verify(c => c.InsertOneAsync(It.IsAny<PropertyTraceModel>(), null, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task UpdateAsync_not_found_returns_404()
        {
            SetupFindReturns(Array.Empty<PropertyTraceModel>());

            var res = await _sut.UpdateAsync("x", new PropertyTraceDto { IdProperty = "p1" });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task UpdateAsync_found_replaces_and_returns_ok()
        {
            SetupFindReturns(new[] { new PropertyTraceModel { Id = "t1", IdProperty = "p1" } });

            var replaceRes = new Mock<ReplaceOneResult>();
            replaceRes.SetupGet(r => r.IsAcknowledged).Returns(true);
            _col.Setup(c => c.ReplaceOneAsync(
                    It.IsAny<Expression<Func<PropertyTraceModel, bool>>>(),
                    It.IsAny<PropertyTraceModel>(),
                    It.IsAny<ReplaceOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(replaceRes.Object);

            var res = await _sut.UpdateAsync("t1", new PropertyTraceDto { IdProperty = "p2" });

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(200);
        }

        [Test]
        public async Task PatchAsync_empty_fields_returns_400()
        {
            var res = await _sut.PatchAsync("t1", new Dictionary<string, object>());
            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
        }

        [Test]
        public async Task PatchAsync_not_found_returns_404()
        {
            SetupFindReturns(Array.Empty<PropertyTraceModel>());

            var res = await _sut.PatchAsync("t1", new Dictionary<string, object> { ["price"] = 1000 });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task PatchAsync_valid_updates_and_returns_ok()
        {
            SetupFindReturns(new[] { new PropertyTraceModel { Id = "t1", IdProperty = "p1" } });

            var updateRes = new Mock<UpdateResult>();
            updateRes.SetupGet(u => u.IsAcknowledged).Returns(true);
            _col.Setup(c => c.UpdateOneAsync(
                    It.IsAny<Expression<Func<PropertyTraceModel, bool>>>(),
                    It.IsAny<UpdateDefinition<PropertyTraceModel>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(updateRes.Object);

            var res = await _sut.PatchAsync("t1", new Dictionary<string, object> { ["price"] = 999 });

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(200);
        }

        [Test]
        public async Task DeleteAsync_not_found_returns_404()
        {
            var delRes0 = new Mock<DeleteResult>();
            delRes0.SetupGet(d => d.DeletedCount).Returns(0);
            delRes0.SetupGet(d => d.IsAcknowledged).Returns(true);

            _col.Setup(c => c.DeleteOneAsync(It.IsAny<Expression<Func<PropertyTraceModel, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(delRes0.Object);

            var res = await _sut.DeleteAsync("nope");

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task DeleteAsync_deleted_returns_success()
        {
            var delRes1 = new Mock<DeleteResult>();
            delRes1.SetupGet(d => d.DeletedCount).Returns(1);
            delRes1.SetupGet(d => d.IsAcknowledged).Returns(true);

            _col.Setup(c => c.DeleteOneAsync(It.IsAny<Expression<Func<PropertyTraceModel, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(delRes1.Object);

            var res = await _sut.DeleteAsync("t1");

            res.Success.Should().BeTrue();
        }
    }
}
