using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using RealEstate.API.Modules.Token.Service;
using RealEstate.API.Modules.User.Model;

namespace RealEstate.Tests.Services
{
    [TestFixture]
    public class JwtServiceTests
    {
        private JwtService _jwtService = null!;
        private IConfiguration _config = null!;

        [SetUp]
        public void Setup()
        {
            // ✅ Configuración en memoria simulando appsettings o variables de entorno
            var settings = new Dictionary<string, string?>
            {
                { "JwtSettings:SecretKey", "SuperSecretKeyForTesting123456789" },
                { "JwtSettings:Issuer", "RealEstateAPI" },
                { "JwtSettings:Audience", "RealEstateUsers" },
                { "JwtSettings:ExpiryMinutes", "60" }
            };

            _config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings!)
                .Build();

            _jwtService = new JwtService(_config);
        }

        [Test]
        public void GenerateToken_ShouldReturnValidJwtString()
        {
            // Arrange
            var user = new UserModel
            {
                Id = "user123",
                Name = "Test User",
                Email = "test@example.com",
                Role = "admin",
                Password = "hashedpass"
            };

            // Act
            var token = _jwtService.GenerateToken(user);

            // Assert
            token.Should().NotBeNullOrEmpty("el token JWT no debe ser nulo ni vacío");

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            jwt.Should().NotBeNull();
            jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == user.Name);
            jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == user.Role);
        }

        [Test]
        public void ValidateToken_ShouldReturnClaimsPrincipal_ForValidToken()
        {
            // Arrange
            var user = new UserModel
            {
                Id = "valid123",
                Name = "Valid User",
                Email = "valid@example.com",
                Role = "user",
                Password = "hashedpass"
            };

            var token = _jwtService.GenerateToken(user);

            // Act
            var principal = _jwtService.ValidateToken(token);

            // Assert
            principal.Should().NotBeNull("el token debe ser válido y devolver un ClaimsPrincipal");
            principal!.Identity!.IsAuthenticated.Should().BeTrue();
            principal.FindFirst(ClaimTypes.Name)?.Value.Should().Be(user.Name);
        }

        [Test]
        public void ValidateToken_ShouldReturnNull_ForInvalidToken()
        {
            // Arrange
            var invalidToken = "this.is.not.valid";

            // Act
            var principal = _jwtService.ValidateToken(invalidToken);

            // Assert
            principal.Should().BeNull("el token inválido no debe producir un ClaimsPrincipal");
        }
    }
}
