using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using RealEstate.API.Modules.Owner.Controller;

namespace RealEstate.Tests
{
    [TestFixture]
    public class OwnerControllerTests
    {
        [Test]
        public async Task Patch_ShouldReturnBadRequest_WhenNoFieldsProvided()
        {
            // Arrange: pass null service as it's not used on this path
            var controller = new OwnerController(service: null!);

            // Act
            var resultEmpty = await controller.Patch("any-id", new Dictionary<string, object>());
            var resultNull = await controller.Patch("any-id", null!);

            // Assert
            resultEmpty.Should().BeOfType<BadRequestObjectResult>();
            resultNull.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}

