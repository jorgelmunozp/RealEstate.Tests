using Moq;
using NUnit.Framework;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using RealEstate.API.Modules.Property.Controller;
using RealEstate.API.Modules.Property.Dto;
using RealEstate.API.Modules.Property.Interface;
using RealEstate.API.Infraestructure.Core.Services;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace RealEstate.Tests.Modules.Property.Controller
{
    [TestFixture]
    public class PropertyControllerTests
    {
        private Mock<IPropertyService> _mockPropertyService = null!;
        private PropertyController _controller = null!;

        [SetUp]
        public void SetUp()
        {
            _mockPropertyService = new Mock<IPropertyService>();
            _controller = new PropertyController(_mockPropertyService.Object);
        }

        // GET: api/property
        [Test]
        public async Task GetAll_ShouldReturn200_WithData()
        {
            var propertyDtos = new List<PropertyDto> { new PropertyDto { IdProperty = "1", Name = "Property 1" } };
            var svcResult = ServiceResultWrapper<object>.Ok(propertyDtos, "OK"); // <- object

            _mockPropertyService
                .Setup(s => s.GetCachedAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<int>(),
                    It.IsAny<int>(), It.IsAny<bool>()))
                .Returns(Task.FromResult(svcResult)); // <- evita ReturnsAsync

            var result = await _controller.GetAll(null, null, null, null, null, 1, 6, false);

            var obj = result as ObjectResult;
            obj.Should().NotBeNull();
            obj!.StatusCode.Should().Be(200);

            var payload = obj.Value as ServiceResultWrapper<object>; // <- object
            payload.Should().NotBeNull();
            payload!.Success.Should().BeTrue();
            payload.Data.Should().BeOfType<List<PropertyDto>>();
            ((List<PropertyDto>)payload.Data!).Should().BeEquivalentTo(propertyDtos);
        }

        [Test]
        public async Task GetAll_ShouldReturn400_WhenServiceFails()
        {
            var svcResult = ServiceResultWrapper<object>.Fail("Error", 400); // <- object (usa tu factory de errores)

            _mockPropertyService
                .Setup(s => s.GetCachedAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<int>(),
                    It.IsAny<int>(), It.IsAny<bool>()))
                .Returns(Task.FromResult(svcResult)); // <- evita ReturnsAsync

            var result = await _controller.GetAll(null, null, null, null, null, 1, 6, false);

            var obj = result as ObjectResult;
            obj.Should().NotBeNull();
            obj!.StatusCode.Should().Be(400);

            var payload = obj.Value as ServiceResultWrapper<object>; // <- object
            payload.Should().NotBeNull();
            payload!.Success.Should().BeFalse();
            payload.Message.Should().Be("Error");
        }

        // GET: api/property/{id}
        [Test]
        public async Task GetById_ShouldReturn200_WithItem()
        {
            var dto = new PropertyDto { IdProperty = "1", Name = "P1" };
            var svcResult = ServiceResultWrapper<PropertyDto>.Ok(dto, "OK");

            _mockPropertyService
                .Setup(s => s.GetByIdAsync("1"))
                .Returns(Task.FromResult(svcResult));

            var result = await _controller.GetById("1");

            var obj = result as ObjectResult;
            obj.Should().NotBeNull();
            obj!.StatusCode.Should().Be(200);

            var payload = obj.Value as ServiceResultWrapper<PropertyDto>;
            payload.Should().NotBeNull();
            payload!.Data.Should().BeEquivalentTo(dto);
        }

        [Test]
        public async Task GetById_ShouldReturn404_WhenNotFound()
        {
            var svcResult = ServiceResultWrapper<PropertyDto>.Fail("Property not found", 404);

            _mockPropertyService
                .Setup(s => s.GetByIdAsync("999"))
                .Returns(Task.FromResult(svcResult));

            var result = await _controller.GetById("999");

            var obj = result as ObjectResult;
            obj.Should().NotBeNull();
            obj!.StatusCode.Should().Be(404);

            var payload = obj.Value as ServiceResultWrapper<PropertyDto>;
            payload.Should().NotBeNull();
            payload!.Message.Should().Be("Property not found");
        }

        // POST: api/property
        [Test]
        public async Task Create_ShouldReturn400_WhenBodyIsNull()
        {
            var result = await _controller.Create(null);

            result.Should().BeOfType<BadRequestObjectResult>();
            var bad = result as BadRequestObjectResult;
            bad!.Value.Should().BeEquivalentTo(new { Success = false, Message = "El cuerpo de la solicitud no puede ser nulo." });
        }

        [Test]
        public async Task Create_ShouldReturn201_WhenCreated()
        {
            var dto = new PropertyDto { IdProperty = "1", Name = "P1" };
            var svcResult = ServiceResultWrapper<PropertyDto>.Created(dto, "Created");

            _mockPropertyService
                .Setup(s => s.CreateAsync(It.IsAny<PropertyDto>()))
                .Returns(Task.FromResult(svcResult));

            var result = await _controller.Create(dto);

            var obj = result as ObjectResult;
            obj.Should().NotBeNull();
            obj!.StatusCode.Should().Be(201);

            var payload = obj.Value as ServiceResultWrapper<PropertyDto>;
            payload.Should().NotBeNull();
            payload!.Data.Should().BeEquivalentTo(dto);
        }

        // PUT: api/property/{id}
        [Test]
        public async Task Update_ShouldReturn400_WhenIdIsEmpty()
        {
            var result = await _controller.Update("", new PropertyDto());
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task Update_ShouldReturn200_WhenUpdated()
        {
            var dto = new PropertyDto { IdProperty = "1", Name = "Updated" };
            var svc = ServiceResultWrapper<PropertyDto>.Ok(dto, "Updated");

            _mockPropertyService
                .Setup(s => s.UpdateAsync("1", It.IsAny<PropertyDto>()))
                .Returns(Task.FromResult(svc));

            var result = await _controller.Update("1", dto);

            var obj = result as ObjectResult;
            obj.Should().NotBeNull();
            obj!.StatusCode.Should().Be(200);

            var payload = obj.Value as ServiceResultWrapper<PropertyDto>;
            payload.Should().NotBeNull();
            payload!.Data.Should().BeEquivalentTo(dto);
        }

        // DELETE: api/property/{id}
        [Test]
        public async Task Delete_ShouldReturn400_WhenIdIsEmpty()
        {
            var result = await _controller.Delete("");
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task Delete_ShouldReturn200_WhenDeleted()
        {
            var svc = ServiceResultWrapper<bool>.Ok(true, "Propiedad eliminada correctamente");

            _mockPropertyService
                .Setup(s => s.DeleteAsync("1"))
                .Returns(Task.FromResult(svc));

            var result = await _controller.Delete("1");

            var obj = result as ObjectResult;
            obj.Should().NotBeNull();
            obj!.StatusCode.Should().Be(200);

            var payload = obj.Value as ServiceResultWrapper<bool>;
            payload.Should().NotBeNull();
            payload!.Success.Should().BeTrue();
            payload.Message.Should().Be("Propiedad eliminada correctamente");
        }
    }
}
