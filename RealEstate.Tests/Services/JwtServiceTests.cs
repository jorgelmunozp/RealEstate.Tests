using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using RealEstate.API.Modules.Auth.Service;
using RealEstate.API.Modules.User.Model;

namespace RealEstate.Tests
{
    [TestFixture]
    public class JwtServiceTests
    {
        private JwtService _jwtService = null!;
        private IConfiguration _config = null!;

        [SetUp]
        public void Setup()
        {
            // 🔹 Configuración en memoria consistente con JwtService
            var settings = new Dictionary<string, string>
            {
                { "JwtSettings:SecretKey", "SuperSecretKeyForTesting123456789" },
                { "JwtSettings:Issuer", "RealEstateAPI" },
                { "JwtSettings:Audience", "RealEstateUsers" },
                { "JwtSettings:ExpiryMinutes", "60" } // necesario para GenerateToken
            };

            _config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            _jwtService = new JwtService(_config);
        }

        [Test]
        public void GenerateToken_ShouldReturnValidJwtString()
        {
            // Arrange
            var user = new UserModel
            {
                Id = default!,
                Password = "TestPassword123!",
                Name = "testuser",
                Email = "testuser@example.com",
                Role = "user"
            };

            // Act
            var token = _jwtService.GenerateToken(user);

            // Assert
            token.Should().NotBeNullOrEmpty();

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            jwt.Should().NotBeNull();
            jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == user.Name);
        }

        [Test]
        public void ValidateToken_ShouldReturnTrue_ForValidToken()
        {
            // Arrange
            var user = new UserModel
            {
                Id = default!,
                Password = "ValidPass123!",
                Name = "validuser",
                Email = "valid@example.com",
                Role = "admin"
            };

            var token = _jwtService.GenerateToken(user);

            // Act
            var isValid = _jwtService.ValidateToken(token);

            // Assert
            isValid.Should().BeTrue();
        }

        [Test]
        public void ValidateToken_ShouldReturnFalse_ForInvalidToken()
        {
            // Arrange
            var invalidToken = "this.is.not.valid";

            // Act
            var isValid = _jwtService.ValidateToken(invalidToken);

            // Assert
            isValid.Should().BeFalse();
        }
    }
}
