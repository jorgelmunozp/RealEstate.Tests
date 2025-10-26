using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using RealEstate.API.Modules.Property.Controller;
using RealEstate.API.Modules.Property.Dto;

namespace RealEstate.Tests
{
    [TestFixture]
    public class PropertyControllerTests
    {
        [Test]
        public async Task Create_ShouldReturnBadRequest_WhenBodyIsNull()
        {
            // Arrange: controller with no service usage on null body path
            var controller = new PropertyController(service: null!);

            // Act
            var result = await controller.Create(null!);

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task Update_ShouldReturnBadRequest_WhenBodyIsNull()
        {
            // Arrange
            var controller = new PropertyController(service: null!);

            // Act
            var result = await controller.Update("any-id", null!);

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task Patch_ShouldReturnBadRequest_WhenNoFieldsProvided()
        {
            // Arrange
            var controller = new PropertyController(service: null!);

            // Act
            var resultEmpty = await controller.Patch("any-id", new Dictionary<string, object>());
            var resultNull = await controller.Patch("any-id", null!);

            // Assert
            resultEmpty.Should().BeOfType<BadRequestObjectResult>();
            resultNull.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}

