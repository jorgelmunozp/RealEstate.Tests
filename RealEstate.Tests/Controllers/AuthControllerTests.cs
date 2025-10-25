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
        private Mock<IAuthService> _authService = null!;
        private Mock<IValidator<LoginDto>> _loginValidator = null!;
        private Mock<IValidator<UserDto>> _userValidator = null!;
        private AuthController _controller = null!;

        [SetUp]
        public void Setup()
        {
            _authService = new Mock<IAuthService>();
            _loginValidator = new Mock<IValidator<LoginDto>>();
            _userValidator = new Mock<IValidator<UserDto>>();

            _loginValidator
                .Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), default))
                .ReturnsAsync(new ValidationResult());

            _userValidator
                .Setup(v => v.ValidateAsync(It.IsAny<UserDto>(), default))
                .ReturnsAsync(new ValidationResult());

            _controller = new AuthController(_authService.Object, _loginValidator.Object, _userValidator.Object);
        }

        [Test]
        public async Task Register_ShouldReturnOk_WhenValidUser()
        {
            // Arrange
            _authService
                .Setup(s => s.RegisterAsync(It.IsAny<UserDto>()))
                .ReturnsAsync(new ValidationResult());

            var request = new UserDto { Name = "Usuario Nuevo", Email = "nuevo@correo.com", Password = "123456", Role = "user" };

            // Act
            var result = await _controller.Register(request);

            // Assert
            result.Should().BeOfType<OkObjectResult>();
        }

        [Test]
        public async Task Register_ShouldReturnBadRequest_WhenValidationErrors()
        {
            // Arrange
            var vr = new ValidationResult(new[] { new ValidationFailure("Email", "El email ya está registrado") });
            _authService
                .Setup(s => s.RegisterAsync(It.IsAny<UserDto>()))
                .ReturnsAsync(vr);

            var request = new UserDto { Name = "Duplicado", Email = "existente@correo.com", Password = "abc123", Role = "user" };

            // Act
            var result = await _controller.Register(request);

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task Login_ShouldReturnOkWithToken_WhenCredentialsAreValid()
        {
            // Arrange
            _authService
                .Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
                .ReturnsAsync("FAKE.JWT.TOKEN");

            var request = new LoginDto { Email = "user@test.com", Password = "123456" };

            // Act
            var result = await _controller.Login(request);

            // Assert
            var okResult = result as OkObjectResult;
            okResult.Should().NotBeNull();

            var value = okResult!.Value as dynamic;
            ((string)value!.Token).Should().Be("FAKE.JWT.TOKEN");
        }

        [Test]
        public async Task Login_ShouldReturnUnauthorized_WhenAuthServiceThrowsInvalidOperation()
        {
            // Arrange
            _authService
                .Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
                .ThrowsAsync(new InvalidOperationException("Usuario o contraseña incorrectos"));

            var request = new LoginDto { Email = "noexiste@test.com", Password = "123456" };

            // Act
            var result = await _controller.Login(request);

            // Assert
            result.Should().BeOfType<UnauthorizedObjectResult>();
        }
    }
}
