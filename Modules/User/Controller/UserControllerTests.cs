using Moq;
using NUnit.Framework;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using RealEstate.API.Modules.User.Controller;
using RealEstate.API.Modules.User.Dto;
using RealEstate.API.Modules.User.Interface;
using RealEstate.API.Infraestructure.Core.Services;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace RealEstate.Tests.Modules.User.Controller
{
    [TestFixture]
    public class UserControllerTests
    {
        private Mock<IUserService> _mockUserService = null!;
        private UserController _controller = null!;
        private ClaimsPrincipal _user = null!;

        [SetUp]
        public void SetUp()
        {
            _mockUserService = new Mock<IUserService>();
            _controller = new UserController(_mockUserService.Object);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Role, "admin"),
                new Claim(ClaimTypes.Email, "user@example.com")
            };
            _user = new ClaimsPrincipal(new ClaimsIdentity(claims, "mock"));
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = _user }
            };
        }

        // ===========================================================
        // GET: api/user
        // ===========================================================
        [Test]
        public async Task GetAll_ShouldReturnOkResult_WhenServiceReturnsSuccess()
        {
            var userDtos = new List<UserDto> { new UserDto { Email = "user@example.com", Name = "John" } };
            _mockUserService.Setup(s => s.GetAllAsync(It.IsAny<bool>()))
                .ReturnsAsync(ServiceResultWrapper<List<UserDto>>.Ok(userDtos));

            var result = await _controller.GetAll(refresh: false);

            result.Should().BeOfType<OkObjectResult>();
            var okResult = result as OkObjectResult;
            okResult.Should().NotBeNull();
            okResult!.Value.Should().BeEquivalentTo(userDtos);
        }

        // ===========================================================
        // POST: api/user
        // ===========================================================
        [Test]
        public async Task Create_ShouldReturnBadRequest_WhenDtoIsNull()
        {
            var result = await _controller.Create(null!); // suprime CS8625

            result.Should().BeOfType<BadRequestObjectResult>();
            var badRequestResult = result as BadRequestObjectResult;
            badRequestResult.Should().NotBeNull();
            badRequestResult!.Value.Should().BeEquivalentTo(new { Success = false, Message = "El cuerpo de la solicitud no puede ser nulo." });
        }

        // ===========================================================
        // PUT: api/user/{email}
        // ===========================================================
        [Test]
        public async Task Update_ShouldReturnOkResult_WhenUserIsUpdated()
        {
            var userDto = new UserDto { Email = "user@example.com", Name = "Updated Name" };
            _mockUserService.Setup(s => s.UpdateUserAsync(It.IsAny<string>(), It.IsAny<UserDto>(), It.IsAny<string>()))
                .ReturnsAsync(ServiceResultWrapper<UserDto>.Ok(userDto));

            var result = await _controller.Update("user@example.com", userDto);

            result.Should().BeOfType<OkObjectResult>();
            var okResult = result as OkObjectResult;
            okResult.Should().NotBeNull();
            okResult!.Value.Should().BeEquivalentTo(userDto);
        }

        [Test]
        public async Task Update_ShouldReturnForbidden_WhenUserIsNotAuthorized()
        {
            var userDto = new UserDto { Email = "user@example.com", Name = "Updated Name" };
            _mockUserService.Setup(s => s.UpdateUserAsync(It.IsAny<string>(), It.IsAny<UserDto>(), It.IsAny<string>()))
                .ReturnsAsync(ServiceResultWrapper<UserDto>.Fail("Forbidden", 403));

            var result = await _controller.Update("user@example.com", userDto);

            result.Should().BeOfType<ForbidResult>();
        }

        // ===========================================================
        // DELETE: api/user/{email}
        // ===========================================================
        [Test]
        public async Task Delete_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            var errorResult = new ServiceResultWrapper<bool>(false, 404, false, "User not found");
            _mockUserService.Setup(s => s.DeleteUserAsync(It.IsAny<string>()))
                .ReturnsAsync(errorResult);

            var result = await _controller.Delete("nonexistent@example.com");

            result.Should().BeOfType<NotFoundObjectResult>();
            var notFoundResult = result as NotFoundObjectResult;
            notFoundResult.Should().NotBeNull();
            notFoundResult!.Value.Should().BeEquivalentTo(new { Message = "User not found" });
        }

        [Test]
        public async Task Delete_ShouldReturnNoContent_WhenUserIsDeleted()
        {
            var deleteResult = new ServiceResultWrapper<bool>(true, 200, true, "User deleted successfully");
            _mockUserService.Setup(s => s.DeleteUserAsync(It.IsAny<string>()))
                .ReturnsAsync(deleteResult);

            var result = await _controller.Delete("user@example.com");

            result.Should().BeOfType<NoContentResult>();
        }
    }
}
