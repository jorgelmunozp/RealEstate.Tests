using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using MongoDB.Driver;
using NUnit.Framework;
using RealEstate.API.Infraestructure.Core.Services;
using RealEstate.API.Modules.Password.Service;
using RealEstate.API.Modules.Token.Interface;
using RealEstate.API.Modules.User.Model;

namespace RealEstate.Tests.Modules.Password.Service
{
    [TestFixture]
    public class PasswordServiceTests
    {
        private Mock<IMongoDatabase> _db = null!;
        private Mock<IMongoCollection<UserModel>> _col = null!;
        private Mock<IJwtService> _jwt = null!;
        private IConfiguration _config = null!;
        private PasswordService _sut = null!;

        [SetUp]
        public void SetUp()
        {
            // Limpiar posibles vars de entorno que puedan interferir con GetEnv
            ClearEnv("SMTP_HOST","SMTP_PORT","SMTP_USER","SMTP_PASS","FRONTEND_URL",
                     "JwtSettings:SecretKey","JWT_SECRET","JWT_ISSUER","JWT_AUDIENCE");

            _db = new Mock<IMongoDatabase>();
            _col = new Mock<IMongoCollection<UserModel>>();
            _jwt = new Mock<IJwtService>();

            // Config mínima para colección y JWT (para generar token cuando haga falta)
            _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["MONGO_COLLECTION_USER"] = "User",
                ["FRONTEND_URL"] = "http://localhost:3000",
                ["JWT_SECRET"] = "supersecret_key_32_chars_min_length",
                ["JWT_ISSUER"] = "RealEstateAPI",
                ["JWT_AUDIENCE"] = "UsuariosAPI"
            }).Build();

            _db.Setup(d => d.GetCollection<UserModel>("User", null)).Returns(_col.Object);

            _sut = new PasswordService(_db.Object, _config, _jwt.Object);
        }

        [TearDown]
        public void TearDown()
        {
            // nada
        }

        // ----------------- helpers Mongo -----------------
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
            var find = new Mock<IFindFluent<T,T>>();
            find.Setup(f => f.ToCursor(It.IsAny<CancellationToken>())).Returns(cursor.Object);
            find.Setup(f => f.ToCursorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cursor.Object);
            return find;
        }

        private void SetupFindByExprReturns(IEnumerable<UserModel> items)
        {
            var find = BuildFindFluent(items);
            _col.Setup(c => c.Find(It.IsAny<Expression<Func<UserModel,bool>>>(), It.IsAny<FindOptions>()))
                .Returns(find.Object);
        }

        private static void ClearEnv(params string[] keys)
        {
            foreach (var k in keys) Environment.SetEnvironmentVariable(k, null);
        }

        // ----------------- SendPasswordRecoveryEmail -----------------

        [Test]
        public void SendPasswordRecoveryEmail_email_vacio_lanza_ArgumentException()
        {
            Func<Task> act = () => _sut.SendPasswordRecoveryEmail(string.Empty);
            act.Should().ThrowAsync<ArgumentException>()
               .WithMessage("*correo electrónico es*");
        }

        [Test]
        public void SendPasswordRecoveryEmail_user_inexistente_lanza_InvalidOperationException()
        {
            SetupFindByExprReturns(Array.Empty<UserModel>());
            Func<Task> act = () => _sut.SendPasswordRecoveryEmail("nobody@mail.com");
            act.Should().ThrowAsync<InvalidOperationException>()
               .WithMessage("*No existe usuario con el email nobody@mail.com*");
        }

        [Test]
        public void SendPasswordRecoveryEmail_smtp_incompleto_lanza_InvalidOperationException()
        {
            // Encontramos usuario -> pasa a generar token -> falla por SMTP incompleto ANTES de enviar
            SetupFindByExprReturns(new[] { new UserModel { Id = "u1", Email = "a@b.com", Name = "Alice" } });

            // Aseguramos que la config SMTP esté incompleta y que no la "salve" una env var
            ClearEnv("SMTP_HOST","SMTP_PORT","SMTP_USER","SMTP_PASS");
            // (tenemos JWT_SECRET en _config, así que GenerateResetToken no falla)

            Func<Task> act = () => _sut.SendPasswordRecoveryEmail("a@b.com");
            act.Should().ThrowAsync<InvalidOperationException>()
               .WithMessage("*Configuración SMTP incompleta*");
        }

        // ----------------- VerifyResetToken -----------------

        [Test]
        public void VerifyResetToken_token_vacio_lanza_InvalidOperationException()
        {
            Action act = () => _sut.VerifyResetToken("");
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*Token requerido*");
        }

        [Test]
        public void VerifyResetToken_invalido_lanza_InvalidOperationException()
        {
            _jwt.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns((ClaimsPrincipal?)null);
            Action act = () => _sut.VerifyResetToken("x.y.z");
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*inválido o expirado*");
        }

        [Test]
        public void VerifyResetToken_valido_devuelve_id()
        {
            var id = "user-123";
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, id)
                // si quisieras probar "sub": new Claim("sub", id)
            }, "jwt");
            var principal = new ClaimsPrincipal(identity);
            _jwt.Setup(j => j.ValidateToken("tok")).Returns(principal);

            var result = _sut.VerifyResetToken("tok");
            var prop = result.GetType().GetProperty("id");
            prop.Should().NotBeNull();
            prop!.GetValue(result)?.ToString().Should().Be(id);
        }

        // ----------------- UpdatePasswordById -----------------

        [Test]
        public async Task UpdatePasswordById_id_vacio_fail()
        {
            var res = await _sut.UpdatePasswordById("", "newpass");
            res.Success.Should().BeFalse();
            res.Message.Should().Contain("ID de usuario es requerido");
        }

        [Test]
        public async Task UpdatePasswordById_password_vacia_fail()
        {
            var res = await _sut.UpdatePasswordById("u1", "");
            res.Success.Should().BeFalse();
            res.Message.Should().Contain("nueva contraseña es requerida");
        }

        [Test]
        public async Task UpdatePasswordById_user_no_encontrado_fail()
        {
            SetupFindByExprReturns(Array.Empty<UserModel>());

            var res = await _sut.UpdatePasswordById("u1", "secret#1");

            res.Success.Should().BeFalse();
            res.Message.Should().Contain("Usuario no encontrado");
        }

        [Test]
        public async Task UpdatePasswordById_ok_actualiza_y_ok()
        {
            // Find devuelve usuario existente
            SetupFindByExprReturns(new[] { new UserModel { Id = "u1", Email = "a@b.com", Name = "A" } });

            // UpdateOneAsync acknowledge
            var updateRes = new Mock<UpdateResult>();
            updateRes.SetupGet(u => u.IsAcknowledged).Returns(true);
            _col.Setup(c => c.UpdateOneAsync(
                    It.IsAny<Expression<Func<UserModel, bool>>>(),
                    It.IsAny<UpdateDefinition<UserModel>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(updateRes.Object);

            var res = await _sut.UpdatePasswordById("u1", "secret#1");

            res.Success.Should().BeTrue();
            res.Data.Should().Be("Contraseña actualizada exitosamente.");
            _col.Verify(c => c.UpdateOneAsync(
                It.IsAny<Expression<Func<UserModel, bool>>>(),
                It.IsAny<UpdateDefinition<UserModel>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
