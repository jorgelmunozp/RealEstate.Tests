using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using RealEstate.API.Modules.Password.Controller;
using RealEstate.API.Modules.Password.Dto;
using MongoDB.Driver;

namespace RealEstate.Tests
{
    [TestFixture]
    public class PasswordControllerTests
    {
        private Mock<IMongoDatabase> _db = null!;
        private IConfiguration _config = null!;

        [SetUp]
        public void Setup()
        {
            _db = new Mock<IMongoDatabase>();

            var inMemory = new System.Collections.Generic.Dictionary<string, string?>
            {
                { "MONGO_COLLECTION_USER", "Users" },
                { "JwtSettings:SecretKey", "SuperSecretKeyForTesting123456789" },
                { "JwtSettings:Issuer", "RealEstateAPI" },
                { "JwtSettings:Audience", "UsuariosAPI" }
            };
            _config = new ConfigurationBuilder().AddInMemoryCollection(inMemory!).Build();
        }

        [Test]
        public async Task Recover_ShouldReturnBadRequest_WhenEmailMissing()
        {
            // Arrange
            var controller = new PasswordController(_db.Object, _config);

            // Act
            var resultNull = await controller.Recover(null!);
            var resultEmpty = await controller.Recover(new PasswordRecoverDto { Email = "" });

            // Assert
            resultNull.Should().BeOfType<BadRequestObjectResult>();
            resultEmpty.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task Update_ShouldReturnBadRequest_WhenTokenOrPasswordMissing()
        {
            // Arrange
            var controller = new PasswordController(_db.Object, _config);

            // Act
            var noDto = await controller.Update(null!);
            var noToken = await controller.Update(new PasswordUpdateDto { Token = "", NewPassword = "abc" });
            var noPassword = await controller.Update(new PasswordUpdateDto { Token = "tok", NewPassword = "" });

            // Assert
            noDto.Should().BeOfType<BadRequestObjectResult>();
            noToken.Should().BeOfType<BadRequestObjectResult>();
            noPassword.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}

