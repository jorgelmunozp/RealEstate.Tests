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
using RealEstate.API.Modules.PropertyImage.Dto;
using RealEstate.API.Modules.PropertyImage.Model;
using RealEstate.API.Modules.PropertyImage.Service;

namespace RealEstate.Tests.Modules.PropertyImage.Service
{
    [TestFixture]
    public class PropertyImageServiceTests
    {
        private Mock<IMongoDatabase> _db = null!;
        private Mock<IMongoCollection<PropertyImageModel>> _col = null!;
        private Mock<IMapper> _mapper = null!;
        private IMemoryCache _cache = null!;
        private IConfiguration _config = null!;
        private IValidator<PropertyImageDto> _validator = null!;
        private PropertyImageService _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _db = new Mock<IMongoDatabase>();
            _col = new Mock<IMongoCollection<PropertyImageModel>>();
            _mapper = new Mock<IMapper>();
            _cache = new MemoryCache(new MemoryCacheOptions());

            _validator = Mock.Of<IValidator<PropertyImageDto>>(v =>
                v.ValidateAsync(It.IsAny<PropertyImageDto>(), It.IsAny<CancellationToken>())
                 == Task.FromResult(new ValidationResult()));

            _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MONGO_COLLECTION_PROPERTYIMAGE"] = "PropertyImage",
                ["CACHE_TTL_MINUTES"] = "1"
            }).Build();

            _db.Setup(d => d.GetCollection<PropertyImageModel>("PropertyImage", null)).Returns(_col.Object);

            _mapper.Setup(m => m.Map<PropertyImageDto>(It.IsAny<PropertyImageModel>()))
                   .Returns<PropertyImageModel>(x => new PropertyImageDto
                   {
                       IdPropertyImage = x.Id,
                       IdProperty = x.IdProperty,
                       File = x.File,
                       Enabled = x.Enabled
                   });

            _mapper.Setup(m => m.Map<IEnumerable<PropertyImageDto>>(It.IsAny<IEnumerable<PropertyImageModel>>()))
                   .Returns<IEnumerable<PropertyImageModel>>(list => list.Select(x => new PropertyImageDto
                   {
                       IdPropertyImage = x.Id,
                       IdProperty = x.IdProperty,
                       File = x.File,
                       Enabled = x.Enabled
                   }).ToList());

            _mapper.Setup(m => m.Map<PropertyImageModel>(It.IsAny<PropertyImageDto>()))
                   .Returns<PropertyImageDto>(d => new PropertyImageModel
                   {
                       Id = string.IsNullOrWhiteSpace(d.IdPropertyImage) ? Guid.NewGuid().ToString("N") : d.IdPropertyImage!,
                       IdProperty = d.IdProperty,
                       File = d.File,
                       Enabled = d.Enabled
                   });

            _mapper.Setup(m => m.Map(It.IsAny<PropertyImageDto>(), It.IsAny<PropertyImageModel>()))
                   .Returns<PropertyImageDto, PropertyImageModel>((src, dest) =>
                   {
                       dest.IdProperty = src.IdProperty;
                       dest.File = src.File;
                       dest.Enabled = src.Enabled;
                       return dest;
                   });

            _sut = new PropertyImageService(_db.Object, _validator, _config, _cache, _mapper.Object);
        }

        [TearDown] public void TearDown() => (_cache as MemoryCache)?.Dispose();

        // ---------- Mongo helpers ----------
        private static Mock<IAsyncCursor<T>> BuildCursor<T>(IEnumerable<T> items)
        {
            var queue = new Queue<IEnumerable<T>>();
            queue.Enqueue(items);
            queue.Enqueue(Array.Empty<T>());

            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
            cursor.Setup(c => c.Current).Returns(() => queue.Peek());
            return cursor;
        }

        private static Mock<IFindFluent<T,T>> BuildFindFluent<T>(IEnumerable<T> items)
        {
            var cursor = BuildCursor(items);
            var find = new Mock<IFindFluent<T, T>>();
            find.Setup(f => f.ToCursor(It.IsAny<CancellationToken>())).Returns(cursor.Object);
            find.Setup(f => f.ToCursorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cursor.Object);
            find.Setup(f => f.Skip(It.IsAny<int?>())).Returns(find.Object);
            find.Setup(f => f.Limit(It.IsAny<int?>())).Returns(find.Object);
            return find;
        }

        private void SetupFindByFilterReturns(IEnumerable<PropertyImageModel> items)
        {
            var find = BuildFindFluent(items);
            _col.Setup(c => c.Find(It.IsAny<FilterDefinition<PropertyImageModel>>(), It.IsAny<FindOptions>()))
                .Returns(find.Object);
        }

        private void SetupFindByExprReturns(IEnumerable<PropertyImageModel> items)
        {
            var find = BuildFindFluent(items);
            _col.Setup(c => c.Find(It.IsAny<Expression<Func<PropertyImageModel, bool>>>(), It.IsAny<FindOptions>()))
                .Returns(find.Object);
        }

        private void SetupFindByExprReturnsSequence(params IEnumerable<PropertyImageModel>[] pages)
        {
            var seq = _col.SetupSequence(c => c.Find(It.IsAny<Expression<Func<PropertyImageModel, bool>>>(), It.IsAny<FindOptions>()));
            foreach (var page in pages) seq = seq.Returns(BuildFindFluent(page).Object);
        }

        // ---------- Tests: GetAll ----------
        [Test]
        public async Task GetAllAsync_fetches_maps_and_caches()
        {
            SetupFindByFilterReturns(new[]
            {
                new PropertyImageModel{ Id="i1", IdProperty="p1", File="a.png", Enabled=true },
                new PropertyImageModel{ Id="i2", IdProperty="p2", File="b.png", Enabled=false }
            });

            var res1 = await _sut.GetAllAsync(page: 1, limit: 6);
            res1.Success.Should().BeTrue();
            res1.Data!.Count().Should().Be(2);

            // segunda llamada con mismos params debe salir de caché (no vuelve a llamar Find)
            var res2 = await _sut.GetAllAsync(page: 1, limit: 6);
            res2.Success.Should().BeTrue();

            _col.Verify(c => c.Find(It.IsAny<FilterDefinition<PropertyImageModel>>(), It.IsAny<FindOptions>()), Times.Once);
        }

        [Test]
        public async Task GetAllAsync_with_filters_and_pagination_calls_skip_limit()
        {
            var find = BuildFindFluent(new[] {
                new PropertyImageModel{ Id="i3", IdProperty="p9", File="c.jpg", Enabled=true }
            });
            _col.Setup(c => c.Find(It.IsAny<FilterDefinition<PropertyImageModel>>(), It.IsAny<FindOptions>()))
                .Returns(find.Object);

            var res = await _sut.GetAllAsync(idProperty: "p9", enabled: true, page: 3, limit: 10, refresh: true);

            res.Success.Should().BeTrue();
            find.Verify(f => f.Skip(20), Times.Once); // (page-1)*limit = 20
            find.Verify(f => f.Limit(10), Times.Once);
        }

        // ---------- Tests: GetById ----------
        [Test]
        public async Task GetByIdAsync_not_found_404()
        {
            SetupFindByExprReturns(Array.Empty<PropertyImageModel>());
            var res = await _sut.GetByIdAsync("nope");
            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task GetByIdAsync_found_ok()
        {
            SetupFindByExprReturns(new[] { new PropertyImageModel{ Id="i1", IdProperty="p1", File="x.png", Enabled=true } });
            var res = await _sut.GetByIdAsync("i1");
            res.Success.Should().BeTrue();
            res.Data!.IdProperty.Should().Be("p1");
        }

        // ---------- Tests: GetByPropertyId ----------
        [Test]
        public async Task GetByPropertyIdAsync_returns_null_when_absent()
        {
            SetupFindByExprReturns(Array.Empty<PropertyImageModel>());
            var dto = await _sut.GetByPropertyIdAsync("pX");
            dto.Should().BeNull();
        }

        [Test]
        public async Task GetByPropertyIdAsync_returns_dto_when_present()
        {
            SetupFindByExprReturns(new[] { new PropertyImageModel{ Id="iZ", IdProperty="pZ", File="z.jpg", Enabled=false } });
            var dto = await _sut.GetByPropertyIdAsync("pZ");
            dto.Should().NotBeNull();
            dto!.IdProperty.Should().Be("pZ");
        }

        // ---------- Tests: CreateAsync ----------
        [Test]
        public async Task CreateAsync_null_body_400()
        {
            var res = await _sut.CreateAsync(null!);
            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
        }

        [Test]
        public async Task CreateAsync_validation_fails_400_no_insert()
        {
            var badValidator = new Mock<IValidator<PropertyImageDto>>();
            badValidator.Setup(v => v.ValidateAsync(It.IsAny<PropertyImageDto>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("File","Requerido") }));
            _sut = new PropertyImageService(_db.Object, badValidator.Object, _config, _cache, _mapper.Object);

            var res = await _sut.CreateAsync(new PropertyImageDto{ IdProperty="p1", File="" });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
            _col.Verify(c => c.InsertOneAsync(It.IsAny<PropertyImageModel>(), null, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task CreateAsync_idProperty_exists_updates_instead_of_insert()
        {
            // 1) Find by IdProperty -> existing
            // 2) UpdateOneAsync
            // 3) Find by Id -> updated
            var existing = new PropertyImageModel{ Id="i1", IdProperty="p1", File="old.png", Enabled=false };
            var updated  = new PropertyImageModel{ Id="i1", IdProperty="p1", File="new.png", Enabled=true };
            SetupFindByExprReturnsSequence(new[]{ existing }, new[]{ updated });

            var updRes = new Mock<UpdateResult>();
            updRes.SetupGet(u => u.IsAcknowledged).Returns(true);
            _col.Setup(c => c.UpdateOneAsync(It.IsAny<Expression<Func<PropertyImageModel,bool>>>(),
                                             It.IsAny<UpdateDefinition<PropertyImageModel>>(),
                                             It.IsAny<UpdateOptions>(),
                                             It.IsAny<CancellationToken>()))
                .ReturnsAsync(updRes.Object);

            var res = await _sut.CreateAsync(new PropertyImageDto{ IdProperty="p1", File="new.png", Enabled=true });

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(200); // Updated
            _col.Verify(c => c.InsertOneAsync(It.IsAny<PropertyImageModel>(), null, It.IsAny<CancellationToken>()), Times.Never);
            _col.Verify(c => c.UpdateOneAsync(It.IsAny<Expression<Func<PropertyImageModel, bool>>>(),
                                              It.IsAny<UpdateDefinition<PropertyImageModel>>(),
                                              It.IsAny<UpdateOptions>(),
                                              It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task CreateAsync_no_existing_inserts_and_returns_201()
        {
            // No existe por IdProperty
            SetupFindByExprReturns(Array.Empty<PropertyImageModel>());

            _col.Setup(c => c.InsertOneAsync(It.IsAny<PropertyImageModel>(), null, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var res = await _sut.CreateAsync(new PropertyImageDto{ IdProperty="p2", File="img.png", Enabled=true });

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(201);
            _col.Verify(c => c.InsertOneAsync(It.IsAny<PropertyImageModel>(), null, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------- Tests: UpdateAsync ----------
        [Test]
        public async Task UpdateAsync_validation_fails_400()
        {
            var badValidator = new Mock<IValidator<PropertyImageDto>>();
            badValidator.Setup(v => v.ValidateAsync(It.IsAny<PropertyImageDto>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("File","bad") }));
            _sut = new PropertyImageService(_db.Object, badValidator.Object, _config, _cache, _mapper.Object);

            var res = await _sut.UpdateAsync("i1", new PropertyImageDto { IdProperty="p1", File="" });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
        }

        [Test]
        public async Task UpdateAsync_not_found_404()
        {
            SetupFindByExprReturns(Array.Empty<PropertyImageModel>());
            var res = await _sut.UpdateAsync("i1", new PropertyImageDto{ IdProperty="p1", File="x" });
            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task UpdateAsync_found_replace_and_ok()
        {
            SetupFindByExprReturns(new[] { new PropertyImageModel{ Id="i1", IdProperty="p1", File="old", Enabled=false } });

            var rep = new Mock<ReplaceOneResult>();
            rep.SetupGet(r => r.IsAcknowledged).Returns(true);
            _col.Setup(c => c.ReplaceOneAsync(It.IsAny<Expression<Func<PropertyImageModel,bool>>>(),
                                              It.IsAny<PropertyImageModel>(),
                                              It.IsAny<ReplaceOptions>(),
                                              It.IsAny<CancellationToken>()))
                .ReturnsAsync(rep.Object);

            var res = await _sut.UpdateAsync("i1", new PropertyImageDto{ IdProperty="p1", File="new", Enabled=true });

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(200);
        }

        // ---------- Tests: PatchAsync ----------
        [Test]
        public async Task PatchAsync_empty_fields_400()
        {
            var res = await _sut.PatchAsync("i1", new Dictionary<string, object>());
            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
        }

        [Test]
        public async Task PatchAsync_not_found_404()
        {
            SetupFindByExprReturns(Array.Empty<PropertyImageModel>());
            var res = await _sut.PatchAsync("iX", new Dictionary<string, object> { ["Enabled"] = true });
            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task PatchAsync_valid_updates_and_returns_updated()
        {
            var existing = new PropertyImageModel{ Id="i1", IdProperty="p1", File="old.png", Enabled=false };
            var updated  = new PropertyImageModel{ Id="i1", IdProperty="p1", File="old.png", Enabled=true };
            SetupFindByExprReturnsSequence(new[]{ existing }, new[]{ updated });

            var up = new Mock<UpdateResult>();
            up.SetupGet(u => u.IsAcknowledged).Returns(true);
            _col.Setup(c => c.UpdateOneAsync(It.IsAny<Expression<Func<PropertyImageModel,bool>>>(),
                                             It.IsAny<UpdateDefinition<PropertyImageModel>>(),
                                             It.IsAny<UpdateOptions>(),
                                             It.IsAny<CancellationToken>()))
                .ReturnsAsync(up.Object);

            var res = await _sut.PatchAsync("i1", new Dictionary<string, object> { ["Enabled"] = true });

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(200);
            res.Data!.Enabled.Should().BeTrue();
        }

        // ---------- Tests: DeleteAsync ----------
        [Test]
        public async Task DeleteAsync_not_found_404()
        {
            var del0 = new Mock<DeleteResult>();
            del0.SetupGet(d => d.IsAcknowledged).Returns(true);
            del0.SetupGet(d => d.DeletedCount).Returns(0);
            _col.Setup(c => c.DeleteOneAsync(It.IsAny<Expression<Func<PropertyImageModel,bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(del0.Object);

            var res = await _sut.DeleteAsync("nope");

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task DeleteAsync_deleted_ok()
        {
            var del1 = new Mock<DeleteResult>();
            del1.SetupGet(d => d.IsAcknowledged).Returns(true);
            del1.SetupGet(d => d.DeletedCount).Returns(1);
            _col.Setup(c => c.DeleteOneAsync(It.IsAny<Expression<Func<PropertyImageModel,bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(del1.Object);

            var res = await _sut.DeleteAsync("i1");

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(200);
        }
    }
}
