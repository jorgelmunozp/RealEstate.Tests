using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using RealEstate.API.Infraestructure.Core.Services;
using RealEstate.API.Modules.Password.Controller;
using RealEstate.API.Modules.Password.Dto;
using RealEstate.API.Modules.Password.Interface;

namespace RealEstate.Tests.Modules.Password.Controller
{
    [TestFixture]
    public class PasswordControllerTests
    {
        private Mock<IPasswordService> _svc = null!;
        private PasswordController _ctrl = null!;

        [SetUp]
        public void SetUp()
        {
            _svc = new Mock<IPasswordService>();
            _ctrl = new PasswordController(_svc.Object);
        }

        // ---------- Helpers para leer propiedades sin asumir el tipo exacto ----------
        private static string? ReadMessage(object v){var t=v.GetType();var p=t.GetProperty("Message")??t.GetProperty("message");return p?.GetValue(v)?.ToString();}
        private static bool? ReadSuccess(object v){var t=v.GetType();var p=t.GetProperty("Success")??t.GetProperty("success");return p is null? null : (bool?)System.Convert.ChangeType(p.GetValue(v), typeof(bool));}
        private static object ReadDataOrSelf(object v){var t=v.GetType();var p=t.GetProperty("Data")??t.GetProperty("data");return p is null? v : p.GetValue(v)!;}
        private static string? ReadProp(object v,string name){var p=v.GetType().GetProperty(name);return p?.GetValue(v)?.ToString();}

        // =========================
        // POST /api/password/recover
        // =========================
        [Test]
        public async Task Recover_ShouldReturnBadRequest_WhenEmailMissing()
        {
            (await _ctrl.Recover(null)).Should().BeOfType<BadRequestObjectResult>();
            (await _ctrl.Recover(new PasswordRecoverDto { Email = "   " }))
                .Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task Recover_ShouldReturnOk_WhenEmailSent()
        {
            // IPasswordService.SendPasswordRecoveryEmail : Task<object>
            var wrapper = ServiceResultWrapper<string>.Ok("sent", "Correo enviado");
            _svc.Setup(s => s.SendPasswordRecoveryEmail("user@test.com"))
                .Returns(Task.FromResult<object>(wrapper));

            var result = await _ctrl.Recover(new PasswordRecoverDto { Email = "user@test.com" });

            var ok = result as OkObjectResult;
            ok.Should().NotBeNull();

            // No asumimos tipo, validamos wrapper o plano
            var val = ok!.Value!;
            ReadSuccess(val).Should().BeTrue();
            ReadMessage(val).Should().Be("Correo enviado");
            var data = ReadDataOrSelf(val);
            ReadProp(data, "Length"); // para strings, Length existe; no afirmamos valor concreto
        }

        [Test]
        public async Task Recover_ShouldReturn500_WhenServiceThrows()
        {
            _svc.Setup(s => s.SendPasswordRecoveryEmail(It.IsAny<string>()))
                .Returns(Task.FromException<object>(new System.Exception("smtp down")));

            var result = await _ctrl.Recover(new PasswordRecoverDto { Email = "x@y.com" });

            var obj = result as ObjectResult;
            obj.Should().NotBeNull();
            obj!.StatusCode.Should().Be(500);
            ReadMessage(obj.Value!).Should().Contain("Error al enviar el correo:");
        }

        // =========================
        // GET /api/password/reset/{token}
        // =========================
        [Test]
        public void VerifyToken_ShouldReturnBadRequest_WhenTokenMissing()
        {
            _ctrl.VerifyToken("").Should().BeOfType<BadRequestObjectResult>();
            _ctrl.VerifyToken("   ").Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public void VerifyToken_ShouldReturnOk_WhenValid()
        {
            var claims = new { id = "u123", email = "user@test.com" };
            _svc.Setup(s => s.VerifyResetToken("good.token")).Returns(claims);

            var result = _ctrl.VerifyToken("good.token");

            var ok = result as OkObjectResult;
            ok.Should().NotBeNull();

            var val = ok!.Value!;
            var data = ReadDataOrSelf(val);
            ReadProp(data, "id").Should().Be("u123");
            ReadProp(data, "email").Should().Be("user@test.com");
        }

        [Test]
        public void VerifyToken_ShouldReturnUnauthorized_WhenServiceThrows()
        {
            _svc.Setup(s => s.VerifyResetToken("bad.token"))
                .Throws(new System.Exception("Token inválido"));

            var result = _ctrl.VerifyToken("bad.token");

            var unauthorized = result as UnauthorizedObjectResult;
            unauthorized.Should().NotBeNull();
            ReadMessage(unauthorized!.Value!).Should().Be("Token inválido");
        }

        // =========================
        // PATCH /api/password/update
        // =========================
        [Test]
        public async Task Update_ShouldReturnBadRequest_WhenDataMissing()
        {
            (await _ctrl.Update(null)).Should().BeOfType<BadRequestObjectResult>();
            (await _ctrl.Update(new PasswordUpdateDto { Token = "", NewPassword = "x" }))
                .Should().BeOfType<BadRequestObjectResult>();
            (await _ctrl.Update(new PasswordUpdateDto { Token = "t", NewPassword = "" }))
                .Should().BeOfType<BadRequestObjectResult>();
        }


        [Test]
        public async Task Update_ShouldReturnOk_WhenUpdated()
        {
            // Devolver ExpandoObject para que verified.id funcione con dynamic
            dynamic verified = new System.Dynamic.ExpandoObject();
            verified.id = "u123";
            _svc.Setup(s => s.VerifyResetToken("good"))
                .Returns((object)verified);

            var okWrapper = ServiceResultWrapper<string>.Ok("updated", "Contraseña actualizada");
            _svc.Setup(s => s.UpdatePasswordById("u123", "New#123"))
                .Returns(Task.FromResult(okWrapper));

            var result = await _ctrl.Update(new PasswordUpdateDto { Token = "good", NewPassword = "New#123" });

            var obj = result as ObjectResult;
            obj.Should().NotBeNull();
            (obj!.StatusCode ?? 200).Should().Be(200);

            var val = obj.Value!;
            ReadSuccess(val).Should().BeTrue();
            ReadMessage(val).Should().Be("Contraseña actualizada");
        }

        [Test]
        public async Task Update_ShouldReturnUnauthorized_WhenVerifyThrowsInvalidOperation()
        {
            _svc.Setup(s => s.VerifyResetToken("bad"))
                .Throws(new System.InvalidOperationException("No autorizado"));

            var result = await _ctrl.Update(new PasswordUpdateDto { Token = "bad", NewPassword = "x" });

            var unauth = result as UnauthorizedObjectResult;
            unauth.Should().NotBeNull();
            ReadMessage(unauth!.Value!).Should().Be("No autorizado");
        }

        [Test]
        public async Task Update_ShouldReturn500_WhenUpdateFails()
        {
            // Igual acá: ExpandoObject
            dynamic verified = new System.Dynamic.ExpandoObject();
            verified.id = "u999";
            _svc.Setup(s => s.VerifyResetToken(It.IsAny<string>()))
                .Returns((object)verified);

            _svc.Setup(s => s.UpdatePasswordById("u999", "Strong!1"))
                .Returns(Task.FromException<ServiceResultWrapper<string>>(new System.Exception("db error")));

            var result = await _ctrl.Update(new PasswordUpdateDto { Token = "tok", NewPassword = "Strong!1" });

            var obj = result as ObjectResult;
            obj.Should().NotBeNull();
            obj!.StatusCode.Should().Be(500);
            ReadMessage(obj.Value!).Should().Contain("Error al actualizar la contraseña:");
        }
    }
}
