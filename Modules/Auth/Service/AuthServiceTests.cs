using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Configuration;
using Moq;
using MongoDB.Driver;
using NUnit.Framework;
using RealEstate.API.Modules.Auth.Dto;
using RealEstate.API.Modules.Auth.Interface;
using RealEstate.API.Modules.Auth.Service;
using RealEstate.API.Modules.Token.Interface;
using RealEstate.API.Modules.User.Dto;
using RealEstate.API.Modules.User.Interface;
using RealEstate.API.Modules.User.Model;
using RealEstate.API.Infraestructure.Core.Services; // <- necesario para ServiceResultWrapper

namespace RealEstate.Tests.Modules.Auth.Service
{
    [TestFixture]
    public class AuthServiceTests
    {
        Mock<IMongoDatabase> _db = null!;
        Mock<IMongoCollection<UserModel>> _users = null!;
        Mock<IJwtService> _jwt = null!;
        Mock<IValidator<LoginDto>> _validator = null!;
        Mock<IUserService> _userService = null!;
        IConfiguration _config = null!;

        [SetUp]
        public void Setup()
        {
            _db = new Mock<IMongoDatabase>();
            _users = new Mock<IMongoCollection<UserModel>>();
            _jwt = new Mock<IJwtService>(MockBehavior.Strict);
            _validator = new Mock<IValidator<LoginDto>>();
            _userService = new Mock<IUserService>();

            _config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["MONGO_COLLECTION_USER"] = "Users" })
                .Build();

            _db.Setup(d => d.GetCollection<UserModel>("Users", It.IsAny<MongoCollectionSettings>()))
               .Returns(_users.Object);
        }

        // Evita CS8620 (nulabilidad) devolviendo Task<UserModel> aunque doc sea null (null-forgiving)
        static IFindFluent<UserModel, UserModel> FindReturning(UserModel? doc)
        {
            var find = new Mock<IFindFluent<UserModel, UserModel>>();
            find.Setup(f => f.FirstOrDefaultAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(doc!));
            return find.Object;
        }

        AuthService SUT() => new AuthService(_db.Object, _config, _jwt.Object, _validator.Object, _userService.Object);

        [Test]
        public void Ctor_Sin_MONGO_COLLECTION_USER_Lanza()
        {
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var db = new Mock<IMongoDatabase>();
            db.Setup(d => d.GetCollection<UserModel>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
              .Returns(_users.Object);

            Action act = () => new AuthService(db.Object, cfg, _jwt.Object, _validator.Object, _userService.Object);
            act.Should().Throw<InvalidOperationException>().WithMessage("*MONGO_COLLECTION_USER*");
        }

        [Test]
        public async Task Login_ValidacionInvalida_400()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("email", "email requerido") }));

            var res = await SUT().LoginAsync(new LoginDto { Email = "", Password = "" });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
            res.Message.Should().NotBeNullOrEmpty().And.ContainEquivalentOf("validación");
            _jwt.VerifyNoOtherCalls();
        }

        [Test]
        public async Task Login_UsuarioNoExiste_401()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult());

            _users.Setup(c => c.Find(
                        It.IsAny<Expression<Func<UserModel, bool>>>(),
                        It.IsAny<FindOptions>()))
                  .Returns(FindReturning(null));

            var res = await SUT().LoginAsync(new LoginDto { Email = "x@y.com", Password = "pw" });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(401);
            res.Message.Should().ContainEquivalentOf("incorrectos");
            _jwt.VerifyNoOtherCalls();
        }

        [Test]
        public async Task Register_SinPassword_400()
        {
            var res = await SUT().RegisterAsync(new UserDto { Email = "x@y.com", Name = "X", Password = null });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
            res.Message.Should().ContainEquivalentOf("contraseña");
            _jwt.VerifyNoOtherCalls();
        }

        [Test]
        public async Task Register_EmailDuplicado_400()
        {
            var existing = new UserDto { Email = "x@y.com", Name = "X", Password = "p", Role = "user" };

            // Evita CS0854 (args opcionales) pasando todos explícitos; y usa el ctor posicional (evita CS7036/CS0200).
            _userService
                .Setup(s => s.GetByEmailAsync("x@y.com", It.IsAny<bool>()))
                .ReturnsAsync(new ServiceResultWrapper<UserDto>(
                    success: true,
                    statusCode: 200,
                    data: existing,
                    message: "ok",
                    errors: null
                ));

            var res = await SUT().RegisterAsync(new UserDto { Email = "x@y.com", Name = "X", Password = "123" });

            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(400);
            res.Message.Should().ContainEquivalentOf("registrado");
            _jwt.VerifyNoOtherCalls();
        }
    }
}
