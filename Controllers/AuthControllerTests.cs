using System;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using RealEstate.API.Modules.Auth.Controller;
using RealEstate.API.Modules.Auth.Dto;
using RealEstate.API.Modules.Auth.Service;
using RealEstate.API.Modules.User.Dto;

namespace RealEstate.Tests
{
    [TestFixture]
    public class AuthControllerTests
    {
        private Mock<IAuthService> _authService;
        private Mock<IValidator<LoginDto>> _loginValidator;
        private Mock<IValidator<UserDto>> _userValidator;
        private AuthController _controller;

        [SetUp]
        public void Setup()
        {
            _authService = new Mock<IAuthService>();
            _loginValidator = new Mock<IValidator<LoginDto>>();
            _userValidator = new Mock<IValidator<UserDto>>();

            _loginValidator.Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), default))
                           .ReturnsAsync(new ValidationResult());
            _userValidator.Setup(v => v.ValidateAsync(It.IsAny<UserDto>(), default))
                          .ReturnsAsync(new ValidationResult());

            _controller = new AuthController(_authService.Object, _loginValidator.Object, _userValidator.Object);
        }

        [TearDown]
        public void Teardown()
        {
            _authService.Reset();
            _loginValidator.Reset();
            _userValidator.Reset();
        }

        // ===========================================================
        // 🔹 Test 1: Registro exitoso → OK
        // ===========================================================
        [Test]
        public async Task Register_ShouldReturnOk_WhenValidUser()
        {
            _authService.Setup(s => s.RegisterAsync(It.IsAny<UserDto>()))
                        .ReturnsAsync(new ValidationResult());

            var request = new UserDto { Name = "Usuario Nuevo", Email = "nuevo@correo.com", Password = "123456", Role = "user" };

            var result = await _controller.Register(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        // ===========================================================
        // 🔹 Test 2: Registro con errores → BadRequest
        // ===========================================================
        [Test]
        public async Task Register_ShouldReturnBadRequest_WhenValidationErrors()
        {
            var vr = new ValidationResult(new[] { new ValidationFailure("Email", "El email ya está registrado") });
            _authService.Setup(s => s.RegisterAsync(It.IsAny<UserDto>()))
                        .ReturnsAsync(vr);

            var request = new UserDto { Name = "Duplicado", Email = "existente@correo.com", Password = "abc123", Role = "user" };

            var result = await _controller.Register(request);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ===========================================================
        // 🔹 Test 3: Login exitoso → OK + Token
        // ===========================================================
        [Test]
        public async Task Login_ShouldReturnOkWithToken_WhenCredentialsAreValid()
        {
            _authService.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
                        .ReturnsAsync("FAKE.JWT.TOKEN");

            var request = new LoginDto { Email = "user@test.com", Password = "123456" };

            var result = await _controller.Login(request);

            var okResult = result as OkObjectResult;
            okResult.Should().NotBeNull();

            var tokenProp = okResult!.Value?.GetType().GetProperty("Token");
            tokenProp.Should().NotBeNull();
            var tokenValue = tokenProp!.GetValue(okResult.Value) as string;
            tokenValue.Should().Be("FAKE.JWT.TOKEN");
        }

        // ===========================================================
        // 🔹 Test 4: Login inválido → Unauthorized
        // ===========================================================
        [Test]
        public async Task Login_ShouldReturnUnauthorized_WhenAuthServiceThrowsInvalidOperation()
        {
            _authService.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
                        .ThrowsAsync(new InvalidOperationException("Usuario o contraseña incorrectos"));

            var request = new LoginDto { Email = "noexiste@test.com", Password = "123456" };

            var result = await _controller.Login(request);

            result.Should().BeOfType<UnauthorizedObjectResult>();
            (result as UnauthorizedObjectResult)!.Value.Should().Be("Usuario o contraseña incorrectos");
        }
    }
}
