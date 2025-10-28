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
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using RealEstate.API.Infraestructure.Core.Services;
using RealEstate.API.Modules.Owner.Dto;
using RealEstate.API.Modules.Owner.Model;
using RealEstate.API.Modules.Owner.Service;

namespace RealEstate.Tests.Modules.Owner.Service
{
    [TestFixture]
    public class OwnerServiceTests
    {
        private Mock<IMongoDatabase> _db = null!;
        private Mock<IMongoCollection<OwnerModel>> _col = null!;
        private Mock<IMapper> _mapper = null!;
        private IMemoryCache _cache = null!;
        private IConfiguration _config = null!;
        private IValidator<OwnerDto> _validator = null!;
        private OwnerService _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _db = new Mock<IMongoDatabase>();
            _col = new Mock<IMongoCollection<OwnerModel>>();
            _mapper = new Mock<IMapper>();
            _cache = new MemoryCache(new MemoryCacheOptions());

            // Validador OK por defecto
            _validator = Mock.Of<IValidator<OwnerDto>>(v =>
                v.ValidateAsync(It.IsAny<OwnerDto>(), It.IsAny<CancellationToken>())
                 == Task.FromResult(new ValidationResult()));

            _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MONGO_COLLECTION_OWNER"] = "Owner",
                ["CACHE_TTL_MINUTES"] = "1"
            }).Build();

            _db.Setup(d => d.GetCollection<OwnerModel>("Owner", null)).Returns(_col.Object);

            // Map de Model -> Dto
            _mapper.Setup(m => m.Map<OwnerDto>(It.IsAny<OwnerModel>()))
                   .Returns<OwnerModel>(m => new OwnerDto { Name = m.Name, Address = m.Address });

            // Map de Dto -> Model (Create)
            _mapper.Setup(m => m.Map<OwnerModel>(It.IsAny<OwnerDto>()))
                   .Returns<OwnerDto>(d => new OwnerModel
                   {
                       Id = Guid.NewGuid().ToString("N"),
                       Name = d.Name,
                       Address = d.Address
                   });

            // Map de lista
            _mapper.Setup(m => m.Map<List<OwnerDto>>(It.IsAny<List<OwnerModel>>()))
                   .Returns<List<OwnerModel>>(list => list.Select(x => new OwnerDto { Name = x.Name, Address = x.Address }).ToList());

            // Map (PUT) Dto sobre existente
            _mapper.Setup(m => m.Map(It.IsAny<OwnerDto>(), It.IsAny<OwnerModel>()))
                   .Returns<OwnerDto, OwnerModel>((src, dest) =>
                   {
                       dest.Name = src.Name;
                       dest.Address = src.Address;
                       return dest;
                   });

            _sut = new OwnerService(_db.Object, _validator, _config, _cache, _mapper.Object);
        }

        [TearDown] public void TearDown() => (_cache as MemoryCache)?.Dispose();

        // ---------- Helpers Mongo ----------
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

        private void SetupFindReturns(IEnumerable<OwnerModel> items)
        {
            var find = BuildFindFluent(items);
            _col.Setup(c => c.Find(It.IsAny<FilterDefinition<OwnerModel>>(), It.IsAny<FindOptions>()))
                .Returns(find.Object);
            _col.Setup(c => c.Find(It.IsAny<Expression<Func<OwnerModel, bool>>>(), It.IsAny<FindOptions>()))
                .Returns(find.Object);
        }

        // ---------- Tests ----------

        [Test]
        public async Task GetAsync_NoCache_fetches_maps_and_caches()
        {
            SetupFindReturns(new[]
            {
                new OwnerModel { Id = "o1", Name = "Alice", Address = "Calle 1" },
                new OwnerModel { Id = "o2", Name = "Bob",   Address = "Calle 2" }
            });

            var res = await _sut.GetAsync();

            res.Success.Should().BeTrue();
            res.Data!.Count.Should().Be(2);
            _cache.TryGetValue("owner:-", out _).Should().BeFalse(); // la key real incluye "null-null"
            _cache.TryGetValue("owner:null-null", out _).Should().BeTrue();
            _col.Verify(c => c.Find(It.IsAny<FilterDefinition<OwnerModel>>(), It.IsAny<FindOptions>()), Times.Once);
        }

        [Test]
        public async Task GetAsync_uses_cache_when_present_and_no_refresh()
        {
            var cached = new List<OwnerDto> { new OwnerDto { Name = "Cached", Address = "X" } };
            _cache.Set("owner:Juan-Calle", cached, TimeSpan.FromMinutes(1));

            var res = await _sut.GetAsync("Juan", "Calle");

            res.Success.Should().BeTrue();
            res.Data![0].Name.Should().Be("Cached");
            _col.Verify(c => c.Find(It.IsAny<FilterDefinition<OwnerModel>>(), It.IsAny<FindOptions>()), Times.Never);
        }

        [Test]
        public async Task GetByIdAsync_not_found_returns_404()
        {
            SetupFindReturns(Array.Empty<OwnerModel>());

            var res = await _sut.GetByIdAsync("nope");

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task GetByIdAsync_found_returns_ok_with_dto()
        {
            SetupFindReturns(new[] { new OwnerModel { Id = "o1", Name = "Alice", Address = "Calle 1" } });

            var res = await _sut.GetByIdAsync("o1");

            res.Success.Should().BeTrue();
            res.Data!.Name.Should().Be("Alice");
        }

        [Test]
        public async Task CreateAsync_valid_inserts_and_invalidates_owner_all()
        {
            _col.Setup(c => c.InsertOneAsync(It.IsAny<OwnerModel>(), null, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _cache.Set("owner:all", new object(), TimeSpan.FromMinutes(1));

            var dto = new OwnerDto { Name = "New", Address = "Dir" };
            var res = await _sut.CreateAsync(dto);

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(201);
            _cache.TryGetValue("owner:all", out _).Should().BeFalse();
        }

        [Test]
        public async Task CreateAsync_invalid_returns_400_and_no_insert()
        {
            var validator = new Mock<IValidator<OwnerDto>>();
            validator.Setup(v => v.ValidateAsync(It.IsAny<OwnerDto>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("f","e") }));
            _sut = new OwnerService(_db.Object, validator.Object, _config, _cache, _mapper.Object);

            var res = await _sut.CreateAsync(new OwnerDto { Name = "X" });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
            _col.Verify(c => c.InsertOneAsync(It.IsAny<OwnerModel>(), null, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task UpdateAsync_not_found_returns_404()
        {
            SetupFindReturns(Array.Empty<OwnerModel>());

            var res = await _sut.UpdateAsync("o1", new OwnerDto { Name = "A" });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task UpdateAsync_found_replaces_and_invalidates_cache()
        {
            SetupFindReturns(new[] { new OwnerModel { Id = "o1", Name = "Old", Address = "Dir" } });

            var replaceRes = new Mock<ReplaceOneResult>();
            replaceRes.SetupGet(r => r.IsAcknowledged).Returns(true);
            _col.Setup(c => c.ReplaceOneAsync(It.IsAny<Expression<Func<OwnerModel,bool>>>(),
                                              It.IsAny<OwnerModel>(),
                                              It.IsAny<ReplaceOptions>(),
                                              It.IsAny<CancellationToken>()))
                .ReturnsAsync(replaceRes.Object);

            _cache.Set("owner:all", new object(), TimeSpan.FromMinutes(1));
            _cache.Set("owner:o1", new object(), TimeSpan.FromMinutes(1));

            var res = await _sut.UpdateAsync("o1", new OwnerDto { Name = "New", Address = "Nueva" });

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(200);
            _cache.TryGetValue("owner:all", out _).Should().BeFalse();
            _cache.TryGetValue("owner:o1", out _).Should().BeFalse();
        }

        [Test]
        public async Task PatchAsync_empty_fields_returns_400()
        {
            var res = await _sut.PatchAsync("o1", new Dictionary<string, object>());
            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
        }

        [Test]
        public async Task PatchAsync_not_found_returns_404()
        {
            SetupFindReturns(Array.Empty<OwnerModel>());

            var res = await _sut.PatchAsync("o1", new Dictionary<string, object> { ["Name"] = "X" });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task PatchAsync_valid_updates_and_invalidates_cache()
        {
            // Primer Find (existente)
            SetupFindReturns(new[] { new OwnerModel { Id = "o1", Name = "Old", Address = "Dir" } });

            var updateRes = new Mock<UpdateResult>();
            updateRes.SetupGet(u => u.IsAcknowledged).Returns(true);
            _col.Setup(c => c.UpdateOneAsync(It.IsAny<Expression<Func<OwnerModel,bool>>>(),
                                             It.IsAny<UpdateDefinition<OwnerModel>>(),
                                             It.IsAny<UpdateOptions>(),
                                             It.IsAny<CancellationToken>()))
                .ReturnsAsync(updateRes.Object);

            // Segundo Find (después de update)
            SetupFindReturns(new[] { new OwnerModel { Id = "o1", Name = "New", Address = "Dir 2" } });

            _cache.Set("owner:all", new object(), TimeSpan.FromMinutes(1));
            _cache.Set("owner:o1", new object(), TimeSpan.FromMinutes(1));

            var res = await _sut.PatchAsync("o1", new Dictionary<string, object> { ["Name"] = "New", ["Address"] = "Dir 2" });

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(200);
            _cache.TryGetValue("owner:all", out _).Should().BeFalse();
            _cache.TryGetValue("owner:o1", out _).Should().BeFalse();
        }

        [Test]
        public async Task DeleteAsync_not_found_returns_404()
        {
            var del0 = new Mock<DeleteResult>();
            del0.SetupGet(d => d.IsAcknowledged).Returns(true);
            del0.SetupGet(d => d.DeletedCount).Returns(0);
            _col.Setup(c => c.DeleteOneAsync(It.IsAny<Expression<Func<OwnerModel,bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(del0.Object);

            var res = await _sut.DeleteAsync("nope");

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task DeleteAsync_deleted_returns_success_and_invalidates_cache()
        {
            var del1 = new Mock<DeleteResult>();
            del1.SetupGet(d => d.IsAcknowledged).Returns(true);
            del1.SetupGet(d => d.DeletedCount).Returns(1);
            _col.Setup(c => c.DeleteOneAsync(It.IsAny<Expression<Func<OwnerModel,bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(del1.Object);

            _cache.Set("owner:all", new object(), TimeSpan.FromMinutes(1));
            _cache.Set("owner:o1", new object(), TimeSpan.FromMinutes(1));

            var res = await _sut.DeleteAsync("o1");

            res.Success.Should().BeTrue();
            _cache.TryGetValue("owner:all", out _).Should().BeFalse();
            _cache.TryGetValue("owner:o1", out _).Should().BeFalse();
        }
    }
}
