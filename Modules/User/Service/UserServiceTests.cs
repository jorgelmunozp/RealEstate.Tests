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
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using RealEstate.API.Infraestructure.Core.Services;
using RealEstate.API.Modules.User.Dto;
using RealEstate.API.Modules.User.Interface;
using RealEstate.API.Modules.User.Model;
using RealEstate.API.Modules.User.Service;

namespace RealEstate.Tests.Modules.User.Service
{
    [TestFixture]
    public class UserServiceTests
    {
        Mock<IMongoDatabase> _db = null!;
        Mock<IMongoCollection<UserModel>> _users = null!;
        Mock<IValidator<UserDto>> _validator = null!;
        IMemoryCache _cache = null!;
        IConfiguration _cfg = null!;
        Mock<IMapper> _mapper = null!;
        IUserService _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _db = new Mock<IMongoDatabase>();
            _users = new Mock<IMongoCollection<UserModel>>();
            _validator = new Mock<IValidator<UserDto>>();
            _validator.Setup(v => v.ValidateAsync(It.IsAny<UserDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());

            _mapper = new Mock<IMapper>();
            _cache = new MemoryCache(new MemoryCacheOptions());
            _cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["MONGO_COLLECTION_USER"] = "users",
                ["CACHE_TTL_MINUTES"] = "1"
            }).Build();

            _db.Setup(d => d.GetCollection<UserModel>("users", null)).Returns(_users.Object);

            _mapper.Setup(m => m.Map<List<UserDto>>(It.IsAny<List<UserModel>>()))
                   .Returns((List<UserModel> src) => src?.Select(x => new UserDto{ Id=x.Id, Name=x.Name, Email=x.Email, Role=x.Role, Password=x.Password }).ToList() ?? new List<UserDto>());
            _mapper.Setup(m => m.Map<UserDto>(It.IsAny<UserModel>()))
                   .Returns((UserModel src) => src == null ? null! : new UserDto{ Id=src.Id, Name=src.Name, Email=src.Email, Role=src.Role, Password=src.Password });
            _mapper.Setup(m => m.Map<UserModel>(It.IsAny<UserDto>()))
                   .Returns((UserDto src) => new UserModel{ Id=src.Id, Name=src.Name, Email=src.Email, Role=src.Role, Password=src.Password });
            _mapper.Setup(m => m.Map(It.IsAny<UserDto>(), It.IsAny<UserModel>()))
                   .Returns((UserDto src, UserModel dest) => { if(src.Name!=null)dest.Name=src.Name; if(src.Email!=null)dest.Email=src.Email; if(src.Role!=null)dest.Role=src.Role; if(src.Password!=null)dest.Password=src.Password; return dest; });

            _sut = new UserService(_db.Object, _validator.Object, _cfg, _cache, _mapper.Object);
        }

        // ===== Helpers =====
        static IAsyncCursor<T> Cursor<T>(IEnumerable<T> items)
        {
            var cur = new Mock<IAsyncCursor<T>>();
            var batches = new Queue<IEnumerable<T>>(new[] { items, Enumerable.Empty<T>() });
            cur.SetupGet(x => x.Current).Returns(() => batches.Peek());
            cur.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
            cur.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
            return cur.Object;
        }

        static void SetupFindAsync(Mock<IMongoCollection<UserModel>> col, params IEnumerable<UserModel>[] batches)
        {
            var seqNoSession = col.SetupSequence(c => c.FindAsync(
                It.IsAny<FilterDefinition<UserModel>>(),
                It.IsAny<FindOptions<UserModel, UserModel>>(),
                It.IsAny<CancellationToken>()));
            foreach (var b in batches) seqNoSession = seqNoSession.ReturnsAsync(Cursor(b));

            var seqWithSession = col.SetupSequence(c => c.FindAsync(
                It.IsAny<IClientSessionHandle>(),
                It.IsAny<FilterDefinition<UserModel>>(),
                It.IsAny<FindOptions<UserModel, UserModel>>(),
                It.IsAny<CancellationToken>()));
            foreach (var b in batches) seqWithSession = seqWithSession.ReturnsAsync(Cursor(b));
        }

        static DeleteResult Del(long n)
        {
            var m = new Mock<DeleteResult>();
            m.SetupGet(x => x.IsAcknowledged).Returns(true);
            m.SetupGet(x => x.DeletedCount).Returns(n);
            return m.Object;
        }

        // ===== Tests =====
        [Test]
        public async Task GetAllAsync_ShouldHitDbOnce_ThenCache()
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

            _users.Verify(c => c.FindAsync(It.IsAny<FilterDefinition<UserModel>>(),
                                           It.IsAny<FindOptions<UserModel, UserModel>>(),
                                           It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GetByEmailAsync_ShouldReturnUser_AndCache()
        {
            var model = new UserModel{ Id="1", Name="A", Email="a@x.com", Role="user" };
            SetupFindAsync(_users, new List<UserModel>{ model });

            var r1 = await _sut.GetByEmailAsync("a@x.com");
            r1.Success.Should().BeTrue();
            r1.Data!.Email.Should().Be("a@x.com");

            var r2 = await _sut.GetByEmailAsync("a@x.com");
            r2.Success.Should().BeTrue();
            r2.Data!.Email.Should().Be("a@x.com");

            _users.Verify(c => c.FindAsync(It.IsAny<FilterDefinition<UserModel>>(),
                                           It.IsAny<FindOptions<UserModel, UserModel>>(),
                                           It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GetByIdAsync_ShouldReturnUser_AndCache()
        {
            var model = new UserModel{ Id="u1", Name="A", Email="a@x.com", Role="user" };
            SetupFindAsync(_users, new List<UserModel>{ model });

            var r1 = await _sut.GetByIdAsync("u1");
            r1.Success.Should().BeTrue();
            r1.Data!.Id.Should().Be("u1");

            var r2 = await _sut.GetByIdAsync("u1");
            r2.Success.Should().BeTrue();
            r2.Data!.Id.Should().Be("u1");

            _users.Verify(c => c.FindAsync(It.IsAny<FilterDefinition<UserModel>>(),
                                           It.IsAny<FindOptions<UserModel, UserModel>>(),
                                           It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task CreateUserAsync_ShouldHashPassword_Insert_AndReturn201()
        {
            var dto = new UserDto{ Name="N", Email="n@x.com", Role="user", Password="Plain#1" };
            SetupFindAsync(_users, Enumerable.Empty<UserModel>());

            UserModel? inserted = null;
            _users.Setup(c => c.InsertOneAsync(It.IsAny<UserModel>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
                  .Callback<UserModel,InsertOneOptions,CancellationToken>((m,_,__) => inserted = m)
                  .Returns(Task.CompletedTask);

            var res = await _sut.CreateUserAsync(dto);
            res.StatusCode.Should().Be(201);
            res.Success.Should().BeTrue();
            inserted.Should().NotBeNull();
            inserted!.Password.Should().NotBe(dto.Password);
            inserted.Password!.StartsWith("$2").Should().BeTrue();
        }

        [Test]
        public async Task UpdateUserAsync_ShouldForbidRoleChange_ForNonAdmin()
        {
            var existing = new UserModel{ Id="1", Name="A", Email="a@x.com", Role="user", Password="H" };
            SetupFindAsync(_users, new List<UserModel>{ existing });

            var payload = new UserDto{ Name="A2", Email="a@x.com", Role="admin", Password="X" };
            var res = await _sut.UpdateUserAsync("a@x.com", payload, requesterRole: "user");

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(403);
            res.Message.Should().Contain("Solo un administrador");
        }

        [Test]
        public async Task PatchUserAsync_ShouldHashPassword_AllowAdminRole_AndUpdate()
        {
            var existing = new UserModel{ Id="1", Name="A", Email="a@x.com", Role="user", Password="H" };
            SetupFindAsync(_users,
                new List<UserModel>{ existing },
                Enumerable.Empty<UserModel>(),
                new List<UserModel>{ new UserModel{ Id="1", Name="A2", Email="new@x.com", Role="admin", Password="(hashed)" } }
            );

            _users.Setup(c => c.UpdateOneAsync(It.IsAny<FilterDefinition<UserModel>>(),
                                               It.IsAny<UpdateDefinition<UserModel>>(),
                                               It.IsAny<UpdateOptions>(),
                                               It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Mock.Of<UpdateResult>());

            var fields = new Dictionary<string,object> {
                ["name"] = "A2",
                ["email"] = "new@x.com",
                ["password"] = "New#123",
                ["role"] = "admin"
            };

            var res = await _sut.PatchUserAsync("a@x.com", fields, requesterRole: "admin");
            res.Success.Should().BeTrue();
            res.Message.Should().Contain("parcialmente");
            res.Data!.Email.Should().Be("new@x.com");
            res.Data!.Role.Should().Be("admin");
        }

        [Test]
        public async Task DeleteUserAsync_ShouldReturn404_WhenNotFound()
        {
            _users.Setup(c => c.DeleteOneAsync(It.IsAny<FilterDefinition<UserModel>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Del(0));

            var res = await _sut.DeleteUserAsync("nobody@x.com");
            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(404);
        }

        [Test]
        public async Task DeleteUserAsync_ShouldReturnSuccess_WhenDeleted()
        {
            _users.Setup(c => c.DeleteOneAsync(It.IsAny<FilterDefinition<UserModel>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Del(1));

            var res = await _sut.DeleteUserAsync("a@x.com");
            res.Success.Should().BeTrue();
            res.Message.Should().Contain("eliminado");
        }
    }
}
