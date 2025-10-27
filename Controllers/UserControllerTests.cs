using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using RealEstate.API.Modules.User.Controller;
using RealEstate.API.Modules.User.Service;
using RealEstate.API.Modules.User.Dto;

namespace RealEstate.Tests
{
    [TestFixture]
    public class UserControllerTests
    {
        [Test]
        public async Task Patch_ShouldReturnBadRequest_WhenNoFieldsProvided()
        {
            var controller = new UserController(null!);
            controller.ControllerContext = GetFakeContext("user");

            var resultEmpty = await controller.Patch("user@test.com", new Dictionary<string, object>());
            var resultNull = await controller.Patch("user@test.com", null!);

            resultEmpty.Should().BeOfType<BadRequestObjectResult>();
            resultNull.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task Patch_ShouldReturnForbid_WhenNonAdminTriesToChangeRole()
        {
            var controller = new UserController(new FakeUserService("forbid"));
            controller.ControllerContext = GetFakeContext("editor");

            var fields = new Dictionary<string, object> { { "role", "admin" } };
            var result = await controller.Patch("user@test.com", fields);

            result.Should().BeOfType<ForbidResult>();
        }

        [Test]
        public async Task Patch_ShouldReturnNotFound_WhenUserNotFound()
        {
            var controller = new UserController(new FakeUserService("notfound"));
            controller.ControllerContext = GetFakeContext("admin");

            var fields = new Dictionary<string, object> { { "name", "Nuevo Nombre" } };
            var result = await controller.Patch("user@test.com", fields);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public async Task Patch_ShouldReturnOk_WhenUserUpdatedSuccessfully()
        {
            var controller = new UserController(new FakeUserService("ok"));
            controller.ControllerContext = GetFakeContext("admin");

            var fields = new Dictionary<string, object> { { "name", "Nuevo Nombre" } };
            var result = await controller.Patch("user@test.com", fields);

            result.Should().BeOfType<OkObjectResult>();
            var ok = result as OkObjectResult;
            ok!.Value.Should().BeAssignableTo<UserDto>();
        }

        private static ControllerContext GetFakeContext(string role) =>
            new()
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.Name, "tester"),
                        new Claim(ClaimTypes.Role, role)
                    }, "TestAuth"))
                }
            };
    }

    internal class FakeUserService : UserService
    {
        private readonly string _mode;

        public FakeUserService(string mode)
            : base(null!, null!, null!, null!)
        {
            _mode = mode;
        }

        // 🔹 usamos "new" en lugar de "override" para compilar sin cambiar UserService
        public new async Task<ServiceResult<UserDto>> PatchUserAsync(string email, Dictionary<string, object> fields, string role)
        {
            await Task.Delay(1);

            return _mode switch
            {
                "forbid" => new ServiceResult<UserDto> { Success = false, StatusCode = 403, Message = "Solo admin puede cambiar el rol" },
                "notfound" => new ServiceResult<UserDto> { Success = false, StatusCode = 404, Message = "Usuario no encontrado" },
                "ok" => new ServiceResult<UserDto> { Success = true, StatusCode = 200, Data = new UserDto { Name = "Nuevo Nombre", Email = email } },
                _ => new ServiceResult<UserDto> { Success = false, StatusCode = 400, Message = "Error genérico" }
            };
        }
    }

    internal class ServiceResult<T>
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
    }
}
