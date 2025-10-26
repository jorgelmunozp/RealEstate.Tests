using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using RealEstate.API.Modules.User.Controller;

namespace RealEstate.Tests
{
    [TestFixture]
    public class UserControllerTests
    {
        [Test]
        public async Task Patch_ShouldReturnBadRequest_WhenNoFieldsProvided()
        {
            // Arrange: controller with null service (not used on this path)
            var controller = new UserController(service: null!);

            // Provide a principal to avoid any unexpected auth checks, although not needed here
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
                    {
                        new Claim(ClaimTypes.Name, "tester"),
                        new Claim(ClaimTypes.Role, "user")
                    }, "TestAuth"))
                }
            };

            // Act
            var resultEmpty = await controller.Patch("user@test.com", new Dictionary<string, object>());
            var resultNull = await controller.Patch("user@test.com", null!);

            // Assert
            resultEmpty.Should().BeOfType<BadRequestObjectResult>();
            resultNull.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}

