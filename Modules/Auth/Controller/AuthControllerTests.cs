using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using RealEstate.API.Modules.Auth.Controller;
using RealEstate.API.Modules.Auth.Dto;
using RealEstate.API.Modules.Auth.Interface;
using RealEstate.API.Modules.User.Dto;
using RealEstate.API.Infraestructure.Core.Logs; // <-- donde vive ServiceLogResponseWrapper<>

namespace RealEstate.Tests.Modules.Auth.Controller
{
    [TestFixture]
    public class AuthControllerTests
    {
        private Mock<IAuthService> _authService = null!;
        private AuthController _controller = null!;

        [SetUp]
        public void Setup()
        {
            _authService = new Mock<IAuthService>();
            _controller = new AuthController(_authService.Object);
        }

        // =========================================================
        // Helper: crea un ServiceLogResponseWrapper<object> por reflexión
        // (sirve aunque tenga ctor privado y setters privados)
        // =========================================================
        private static ServiceLogResponseWrapper<object> MakeResponse(
            int statusCode, object data, string message, IEnumerable<string> errors = null)
        {
            var t = typeof(ServiceLogResponseWrapper<object>);
            var inst = (ServiceLogResponseWrapper<object>)System.Activator.CreateInstance(t, true)!;

            void Set(string name, object value)
            {
                var p = t.GetProperty(name);
                if (p == null) return;
                var set = p.SetMethod ?? p.GetSetMethod(true);
                if (set != null) set.Invoke(inst, new[] { value });
            }

            // Intenta setear si existen; si no existen, se ignoran
            Set("StatusCode", statusCode);
            Set("Message", message);
            Set("Data", data);
            if (errors != null) Set("Errors", errors);
            // Si tu wrapper tiene Success, lo inferimos del código
            Set("Success", statusCode >= 200 && statusCode < 300);

            return inst;
        }

        [Test]
        public async Task Login_CuerpoNull_Retorna400()
        {
            var result = await _controller.Login(null);
            result.Should().BeOfType<BadRequestObjectResult>();
            var bad = (BadRequestObjectResult)result;
            bad.StatusCode.Should().Be(400);

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(bad.Value));
            doc.RootElement.GetProperty("Message").GetString()
               .Should().Be("El cuerpo de la solicitud no puede estar vacío.");
        }

        [Test]
        public async Task Login_Exitoso_Retorna200()
        {
            var dto = new LoginDto { Email = "demo@acme.com", Password = "123456" };
            var user = new UserDto { Id = "u1", Name = "Demo", Email = "demo@acme.com", Role = "user" };

            var serviceRes = MakeResponse(200, user, "Login exitoso");

            _authService
                .Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
                .Returns(Task.FromResult(serviceRes)); // evita la sobrecarga conflictiva de ReturnsAsync

            var action = await _controller.Login(dto);
            action.Should().BeOfType<ObjectResult>();
            var ok = (ObjectResult)action;
            ok.StatusCode.Should().Be(200);

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
            doc.RootElement.GetProperty("Message").GetString().Should().Be("Login exitoso");
            doc.RootElement.GetProperty("Data").GetProperty("Email").GetString().Should().Be("demo@acme.com");
        }

        [Test]
        public async Task Login_CredencialesInvalidas_Retorna401()
        {
            var serviceRes = MakeResponse(401, null, "Credenciales inválidas");

            _authService
                .Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
                .Returns(Task.FromResult(serviceRes));

            var action = await _controller.Login(new LoginDto { Email = "x", Password = "y" });
            action.Should().BeOfType<UnauthorizedObjectResult>();
            var unauth = (UnauthorizedObjectResult)action;
            unauth.StatusCode.Should().Be(401);

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(unauth.Value));
            doc.RootElement.GetProperty("Message").GetString().Should().Be("Credenciales inválidas");
        }

        [Test]
        public async Task Register_CuerpoNull_Retorna400()
        {
            var result = await _controller.Register(null);
            result.Should().BeOfType<BadRequestObjectResult>();
            var bad = (BadRequestObjectResult)result;
            bad.StatusCode.Should().Be(400);

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(bad.Value));
            doc.RootElement.GetProperty("Message").GetString()
               .Should().Be("El cuerpo de la solicitud no puede estar vacío.");
        }

        [Test]
        public async Task Register_Exitoso_Retorna201()
        {
            var dto = new UserDto { Id = "u2", Name = "New User", Email = "new@acme.com", Role = "user" };
            var serviceRes = MakeResponse(201, dto, "Usuario creado");

            _authService
                .Setup(s => s.RegisterAsync(It.IsAny<UserDto>()))
                .Returns(Task.FromResult(serviceRes));

            var action = await _controller.Register(dto);
            action.Should().BeOfType<CreatedResult>();
            var created = (CreatedResult)action;
            created.StatusCode.Should().Be(201);

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(created.Value));
            doc.RootElement.GetProperty("Message").GetString().Should().Be("Usuario creado");
            doc.RootElement.GetProperty("Data").GetProperty("Email").GetString().Should().Be("new@acme.com");
        }
    }
}
