using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using RealEstate.API.Infraestructure.Core.Services;
using RealEstate.API.Modules.Token.Service;
using RealEstate.API.Modules.User.Dto;
using RealEstate.API.Modules.User.Interface;
using RealEstate.API.Modules.User.Model;

namespace RealEstate.Tests.Modules.Token.Service
{
    [TestFixture]
    public class JwtServiceTests
    {
        IConfiguration _cfg = null!;
        Mock<IUserService> _userSvc = null!;
        JwtService _sut = null!;
        UserModel _userModel = null!;
        UserDto _userDto = null!;

        [SetUp]
        public void SetUp()
        {
            // Evita que 'sub' se remapee a NameIdentifier, etc.
            JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

            _cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["JwtSettings:SecretKey"] = "super-secret-key-for-tests-1234567890",
                ["JwtSettings:Issuer"] = "RealEstateAPI",
                ["JwtSettings:Audience"] = "UsuariosAPI",
                ["JwtSettings:ExpiryMinutes"] = "15",
                ["JwtSettings:RefreshDays"] = "7"
            }).Build();

            _userSvc = new Mock<IUserService>();
            _sut = new JwtService(_cfg, _userSvc.Object);

            _userModel = new UserModel { Id = "u1", Name = "Alice", Email = "alice@test.com", Role = "admin", Password = "x" };
            _userDto   = new UserDto   { Id = "u1", Name = "Alice", Email = "alice@test.com", Role = "admin", Password = "x" };
        }

        [Test]
        public void GenerateToken_Validate_ShouldReturnPrincipalWithAccessType()
        {
            var token = _sut.GenerateToken(_userModel);
            token.Should().NotBeNullOrWhiteSpace();

            var principal = _sut.ValidateToken(token);
            principal.Should().NotBeNull();
            principal!.FindFirst("type")!.Value.Should().Be("access");
            principal.FindFirst(ClaimTypes.Role)!.Value.Should().Be("admin");
            principal.FindFirst(ClaimTypes.Email)!.Value.Should().Be("alice@test.com");
            principal.FindFirst(ClaimTypes.Name)!.Value.Should().Be("Alice");
            principal.FindFirst("sub")!.Value.Should().Be("u1");
        }

        [Test]
        public void GenerateRefreshToken_Validate_ShouldReturnPrincipalWithRefreshType()
        {
            var token = _sut.GenerateRefreshToken(_userModel);
            token.Should().NotBeNullOrWhiteSpace();

            var principal = _sut.ValidateToken(token);
            principal.Should().NotBeNull();
            principal!.FindFirst("type")!.Value.Should().Be("refresh");
            principal.FindFirst("sub")!.Value.Should().Be("u1");
        }

        [Test]
        public void GenerateTokens_ShouldReturnDifferentStrings()
        {
            var (access, refresh) = _sut.GenerateTokens(_userModel);
            access.Should().NotBeNullOrWhiteSpace();
            refresh.Should().NotBeNullOrWhiteSpace();
            access.Should().NotBe(refresh);
        }

        [Test]
        public void RefreshAccessToken_ShouldThrow_WhenTokenIsNotRefreshType()
        {
            var access = _sut.GenerateToken(_userModel);
            Action act = () => _sut.RefreshAccessToken(access, _userModel);
            act.Should().Throw<Microsoft.IdentityModel.Tokens.SecurityTokenException>()
               .WithMessage("*no es de tipo refresh*");
        }

        [Test]
        public async Task ProcessRefreshTokenAsync_ShouldReturn401_WhenHeaderMissingOrBadFormat()
        {
            var r1 = await _sut.ProcessRefreshTokenAsync(null!);
            r1.Success.Should().BeFalse();
            r1.StatusCode.Should().Be(401);

            var r2 = await _sut.ProcessRefreshTokenAsync("Bad token");
            r2.Success.Should().BeFalse();
            r2.StatusCode.Should().Be(401);
        }

        [Test]
        public async Task ProcessRefreshTokenAsync_ShouldReturn401_WhenRefreshInvalid()
        {
            var r = await _sut.ProcessRefreshTokenAsync("Bearer invalid.token.here");
            r.Success.Should().BeFalse();
            r.StatusCode.Should().Be(401);
            r.Message.Should().MatchRegex("(inválido|expirado)");
        }

        [Test]
        public async Task ProcessRefreshTokenAsync_ShouldReturnOk_WithNewTokensAndUser()
        {
            var refresh = _sut.GenerateRefreshToken(_userModel);

            // Si tu firma es GetByIdAsync(string id, bool refresh=false), incluye el bool en el Setup:
            _userSvc.Setup(s => s.GetByIdAsync("u1", It.IsAny<bool>()))
                    .ReturnsAsync(ServiceResultWrapper<UserDto?>.Ok(_userDto, "ok"));

            var res = await _sut.ProcessRefreshTokenAsync($"Bearer {refresh}");

            res.Success.Should().BeTrue();
            res.StatusCode.Should().Be(200);
            res.Message.Should().Contain("renovado");

            var value = res.Data!;
            var t = value.GetType();
            var accessProp  = t.GetProperty("accessToken")!;
            var refreshProp = t.GetProperty("refreshToken")!;
            var expiresProp = t.GetProperty("expiresIn")!;
            var userProp    = t.GetProperty("user")!;

            var newAccess   = accessProp.GetValue(value)?.ToString();
            var newRefresh  = refreshProp.GetValue(value)?.ToString();
            var expiresIn   = Convert.ToInt32(expiresProp.GetValue(value));
            var returnedUser= userProp.GetValue(value)!;

            newAccess.Should().NotBeNullOrWhiteSpace();
            newRefresh.Should().NotBeNullOrWhiteSpace();
            expiresIn.Should().Be(60 * 15);
            returnedUser.GetType().GetProperty("Id")!.GetValue(returnedUser)!.ToString().Should().Be("u1");
        }

        [Test]
        public async Task ProcessRefreshTokenAsync_ShouldReturn401_WhenUserNotFound()
        {
            var refresh = _sut.GenerateRefreshToken(_userModel);

            _userSvc.Setup(s => s.GetByIdAsync("u1", It.IsAny<bool>()))
                    .ReturnsAsync(ServiceResultWrapper<UserDto?>.Fail("no user", 404));

            var res = await _sut.ProcessRefreshTokenAsync($"Bearer {refresh}");
            res.Success.Should().BeFalse();
            res.StatusCode.Should().Be(401);
            res.Message.Should().Contain("Usuario no encontrado");
        }
    }
}
