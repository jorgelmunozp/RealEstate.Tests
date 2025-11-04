using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;                 
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using RealEstate.API.Modules.User.Dto;
using RealEstate.API.Modules.User.Interface;
using RealEstate.API.Modules.User.Model;
using RealEstate.API.Modules.User.Service;

namespace RealEstate.Tests.Modules.User.Service
{
    [TestFixture]
    public class UserServiceTests
    {
        // ---- Fakes para evitar métodos de fábrica del driver ----
        private sealed class FakeDeleteResult : DeleteResult
        {
            private readonly long _deleted;
            public FakeDeleteResult(long deleted) { _deleted = deleted; }
            public override bool IsAcknowledged => true;
            public override long DeletedCount => _deleted;
        }

        private sealed class FakeUpdateResult : UpdateResult
        {
            private readonly long _matched;
            private readonly long _modified;

            public FakeUpdateResult(long matched, long modified)
            {
                _matched = matched;
                _modified = modified;
            }

            public override bool IsAcknowledged => true;
            public override bool IsModifiedCountAvailable => true;
            public override long MatchedCount => _matched;
            public override long ModifiedCount => _modified;
            public override BsonValue UpsertedId => BsonNull.Value;
        }

        // ---- Infra ----
        private Mock<IMongoDatabase> _db = null!;
        private Mock<IMongoCollection<UserModel>> _users = null!;
        private IMemoryCache _cache = null!;
        private IConfiguration _cfg = null!;
        private Mock<IValidator<UserDto>> _validatorMock = null!;
        private IValidator<UserDto> _validator = null!;
        private Mock<IMapper> _mapperMock = null!;
        private IMapper _mapper = null!;
        private IUserService _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _db = new Mock<IMongoDatabase>(MockBehavior.Strict);
            _users = new Mock<IMongoCollection<UserModel>>(MockBehavior.Strict);

            _cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MONGO_COLLECTION_USER"] = "users",
                ["CACHE_TTL_MINUTES"] = "2"
            }).Build();

            _cache = new MemoryCache(new MemoryCacheOptions());

            _validatorMock = new Mock<IValidator<UserDto>>(MockBehavior.Strict);
            _validatorMock
                .Setup(v => v.ValidateAsync(It.IsAny<UserDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _validator = _validatorMock.Object;

            _mapperMock = new Mock<IMapper>(MockBehavior.Loose);
            // Map list
            _mapperMock.Setup(m => m.Map<List<UserDto>>(It.IsAny<List<UserModel>>()))
                       .Returns((List<UserModel> src) =>
                           src?.Select(x => new UserDto
                           {
                               Id = x.Id, Name = x.Name, Email = x.Email, Role = x.Role, Password = x.Password
                           }).ToList() ?? new List<UserDto>());
            // Map single -> dto
            _mapperMock.Setup(m => m.Map<UserDto>(It.IsAny<UserModel>()))
                       .Returns((UserModel src) => src == null ? null! : new UserDto
                       {
                           Id = src.Id, Name = src.Name, Email = src.Email, Role = src.Role, Password = src.Password
                       });
            // Map dto -> model
            _mapperMock.Setup(m => m.Map<UserModel>(It.IsAny<UserDto>()))
                       .Returns((UserDto src) => new UserModel
                       {
                           Id = src.Id, Name = src.Name, Email = src.Email, Role = src.Role, Password = src.Password
                       });
            // Map dto sobre model (update)
            _mapperMock.Setup(m => m.Map(It.IsAny<UserDto>(), It.IsAny<UserModel>()))
                       .Returns((UserDto src, UserModel dest) =>
                       {
                           if (src.Name != null) dest.Name = src.Name;
                           if (src.Email != null) dest.Email = src.Email;
                           if (src.Role != null) dest.Role = src.Role;
                           if (src.Password != null) dest.Password = src.Password;
                           return dest;
                       });

            _mapper = _mapperMock.Object;

            _db.Setup(d => d.GetCollection<UserModel>("users", It.IsAny<MongoCollectionSettings>()))
               .Returns(_users.Object);

            // ---- IMPORTANTÍSIMO: cubrir AnyAsync() del driver (proyección BsonDocument) ----
            _users.Setup(c => c.FindAsync<BsonDocument>(
                    It.IsAny<FilterDefinition<UserModel>>(),
                    It.IsAny<FindOptions<UserModel, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Cursor(Enumerable.Empty<BsonDocument>()));

            _users.Setup(c => c.FindAsync<BsonDocument>(
                    It.IsAny<IClientSessionHandle>(),
                    It.IsAny<FilterDefinition<UserModel>>(),
                    It.IsAny<FindOptions<UserModel, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Cursor(Enumerable.Empty<BsonDocument>()));
            // -------------------------------------------------------------------------------

            _sut = new UserService(_db.Object, _validator, _cfg, _cache, _mapper);
        }

        // ---- Helpers seguros ----
        private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> items)
        {
            var cur = new Mock<IAsyncCursor<T>>();
            var queue = new Queue<IEnumerable<T>>(new[] { items, Enumerable.Empty<T>() });
            cur.SetupGet(x => x.Current).Returns(() => queue.Peek());
            cur.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>()))
               .Returns(true).Returns(false);
            cur.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(true).ReturnsAsync(false);
            return cur.Object;
        }

        private static void SetupFindAsync(Mock<IMongoCollection<UserModel>> col, params IEnumerable<UserModel>[] batches)
        {
            // Overload sin sesión
            var seq = col.SetupSequence(c => c.FindAsync(
                It.IsAny<FilterDefinition<UserModel>>(),
                It.IsAny<FindOptions<UserModel, UserModel>>(),
                It.IsAny<CancellationToken>()));
            foreach (var b in batches)
                seq = seq.ReturnsAsync(Cursor(b));

            // Overload con sesión (por si el servicio lo usa)
            var seqS = col.SetupSequence(c => c.FindAsync(
                It.IsAny<IClientSessionHandle>(),
                It.IsAny<FilterDefinition<UserModel>>(),
                It.IsAny<FindOptions<UserModel, UserModel>>(),
                It.IsAny<CancellationToken>()));
            foreach (var b in batches)
                seqS = seqS.ReturnsAsync(Cursor(b));
        }

        // ===============================================================
        //                          TESTS
        // ===============================================================

        [Test]
        public async Task GetAllAsync_PrimeroDB_LuegoCache()
        {
            var models = new List<UserModel> {
                new UserModel{ Id="1", Name="A", Email="a@x.com", Role="user" },
                new UserModel{ Id="2", Name="B", Email="b@x.com", Role="admin" }
            };
            SetupFindAsync(_users, models);

            var r1 = await _sut.GetAllAsync();
            r1.Success.Should().BeTrue();
            r1.Data.Should().HaveCount(2);

            var r2 = await _sut.GetAllAsync();
            r2.Success.Should().BeTrue();
            r2.Data.Should().HaveCount(2);

            _users.Verify(c => c.FindAsync(
                It.IsAny<FilterDefinition<UserModel>>(),
                It.IsAny<FindOptions<UserModel, UserModel>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GetByEmailAsync_PrimeroDB_LuegoCache()
        {
            var model = new UserModel { Id = "1", Name = "A", Email = "a@x.com", Role = "user" };
            SetupFindAsync(_users, new List<UserModel> { model });

            var r1 = await _sut.GetByEmailAsync("a@x.com");
            r1.Success.Should().BeTrue();
            r1.Data!.Email.Should().Be("a@x.com");

            var r2 = await _sut.GetByEmailAsync("a@x.com");
            r2.Success.Should().BeTrue();
            r2.Data!.Email.Should().Be("a@x.com");

            _users.Verify(c => c.FindAsync(
                It.IsAny<FilterDefinition<UserModel>>(),
                It.IsAny<FindOptions<UserModel, UserModel>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GetByIdAsync_PrimeroDB_LuegoCache()
        {
            var model = new UserModel { Id = "u1", Name = "A", Email = "a@x.com", Role = "user" };
            SetupFindAsync(_users, new List<UserModel> { model });

            var r1 = await _sut.GetByIdAsync("u1");
            r1.Success.Should().BeTrue();
            r1.Data!.Id.Should().Be("u1");

            var r2 = await _sut.GetByIdAsync("u1");
            r2.Success.Should().BeTrue();
            r2.Data!.Id.Should().Be("u1");

            _users.Verify(c => c.FindAsync(
                It.IsAny<FilterDefinition<UserModel>>(),
                It.IsAny<FindOptions<UserModel, UserModel>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task CreateUserAsync_HasheaPassword_Inserta_201()
        {
            var dto = new UserDto { Name = "N", Email = "n@x.com", Role = "user", Password = "Plain#1" };
            var plain = dto.Password; // <<<<<< guarda el valor original (el servicio puede mutar dto.Password)

            // Comprobación de unicidad: vacío (no existe)
            SetupFindAsync(_users, Enumerable.Empty<UserModel>());

            UserModel? inserted = null;
            _users.Setup(c => c.InsertOneAsync(
                    It.IsAny<UserModel>(),
                    It.IsAny<InsertOneOptions>(),
                    It.IsAny<CancellationToken>()))
                  .Callback<UserModel, InsertOneOptions, CancellationToken>((m, _, __) => inserted = m)
                  .Returns(Task.CompletedTask);

            var res = await _sut.CreateUserAsync(dto);

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(201);
            inserted.Should().NotBeNull();
            inserted!.Password.Should().NotBe(plain);                  // compara contra el plano original
            inserted.Password!.StartsWith("$2").Should().BeTrue();     // típico BCrypt
        }

        [Test]
        public async Task UpdateUserAsync_NonAdmin_NoPuedeCambiarRol()
        {
            var existing = new UserModel { Id = "1", Name = "A", Email = "a@x.com", Role = "user", Password = "H" };
            SetupFindAsync(_users, new List<UserModel> { existing });

            var payload = new UserDto { Name = "A2", Email = "a@x.com", Role = "admin", Password = "X" };
            var res = await _sut.UpdateUserAsync("a@x.com", payload, requesterRole: "user");

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(403);
            res.Message.Should().Contain("Solo un administrador");
        }

        [Test]
        public async Task PatchUserAsync_Admin_PuedeCambiarRol_Y_Actualizar()
        {
            var existing = new UserModel { Id = "1", Name = "A", Email = "a@x.com", Role = "user", Password = "H" };

            // Secuencia de lecturas que pueda hacer el servicio:
            // 1) obtener actual
            // 2) (opcional) revalidación de email libre
            // 3) read-back del actualizado
            SetupFindAsync(_users,
                new List<UserModel> { existing },
                Enumerable.Empty<UserModel>(),
                new List<UserModel> {
                    new UserModel { Id="1", Name="A2", Email="new@x.com", Role="admin", Password="(hashed)" }
                });

            _users.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<UserModel>>(),
                    It.IsAny<UpdateDefinition<UserModel>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FakeUpdateResult(matched: 1, modified: 1));

            var fields = new Dictionary<string, object>
            {
                ["name"] = "A2",
                ["email"] = "new@x.com",
                ["password"] = "New#123",
                ["role"] = "admin"
            };

            var res = await _sut.PatchUserAsync("a@x.com", fields, requesterRole: "admin");

            res.Success.Should().BeTrue();
            res.Data.Should().NotBeNull();                 // <<<<<< evita NRE y deja fallo legible si viniera null
            res.Message.Should().Contain("parcialmente");
            var d = res.Data!;
            d.Email.Should().Be("new@x.com");
            d.Role.Should().Be("admin");
        }

        [Test]
        public async Task DeleteUserAsync_NotFound_404()
        {
            _users.Setup(c => c.DeleteOneAsync(
                    It.IsAny<FilterDefinition<UserModel>>(),
                    It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FakeDeleteResult(0));

            var res = await _sut.DeleteUserAsync("nobody@x.com");

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task DeleteUserAsync_Ok_DevuelveDeleted()
        {
            _users.Setup(c => c.DeleteOneAsync(
                    It.IsAny<FilterDefinition<UserModel>>(),
                    It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new FakeDeleteResult(1));

            var res = await _sut.DeleteUserAsync("a@x.com");

            res.Success.Should().BeTrue();
            res.Message.Should().Contain("eliminado");
        }
    }
}
