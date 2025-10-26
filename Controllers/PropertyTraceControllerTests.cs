using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using RealEstate.API.Modules.PropertyTrace.Controller;
using RealEstate.API.Modules.PropertyTrace.Dto;

namespace RealEstate.Tests
{
    [TestFixture]
    public class PropertyTraceControllerTests
    {
        [Test]
        public async Task Create_ShouldReturnBadRequest_WhenNoTracesProvided()
        {
            // Arrange: service not needed for invalid payload path
            var controller = new PropertyTraceController(service: null!);

            // Act
            var resultNull = await controller.Create(null!);
            var resultEmpty = await controller.Create(Enumerable.Empty<PropertyTraceDto>());

            // Assert
            resultNull.Should().BeOfType<BadRequestObjectResult>();
            resultEmpty.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}

