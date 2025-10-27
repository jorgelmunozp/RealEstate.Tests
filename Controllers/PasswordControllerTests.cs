using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using RealEstate.API.Modules.Password.Controller;
using RealEstate.API.Modules.Password.Dto;
using RealEstate.API.Modules.Password.Service;

namespace RealEstate.Tests.Controllers
{
    [TestFixture]
    public class PasswordControllerTests
    {
        private Mock<PasswordService> _passwordService = null!;

        [SetUp]
        public void Setup()
        {
            _passwordService = new Mock<PasswordService>(null!, null!);
        }

        [Test]
        public async Task Recover_ShouldReturnBadRequest_WhenEmailMissing()
        {
            // Arrange
            var controller = new PasswordController(_passwordService.Object);

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
            var controller = new PasswordController(_passwordService.Object);

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
