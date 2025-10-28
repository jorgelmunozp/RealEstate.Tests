using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstate.API.Infraestructure.Core.Services;
using RealEstate.API.Modules.Token.Interface;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace RealEstate.API.Modules.Token.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class TokenController : ControllerBase
    {
        private readonly IJwtService _jwtService;

        public TokenController(IJwtService jwtService)
        {
            _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
        }

        // =========================================================
        // POST: /api/token/refresh
        // =========================================================
        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> Refresh()
        {
            try
            {
                var authHeader = Request.Headers["Authorization"].ToString();
                var result = await _jwtService.ProcessRefreshTokenAsync(authHeader);
                return StatusCode(result.StatusCode, result);
            }
            catch (Exception ex)
            {
                // 👇 Mensaje exacto que esperan tus tests
                return StatusCode(500, new { Success = false, Message = $"Error al renovar token: {ex.Message}" });
            }
        }

        // =========================================================
        // GET: /api/token/validate?token=...
        // =========================================================
        [HttpGet("validate")]
        [AllowAnonymous]
        public IActionResult Validate([FromQuery] string token)
        {
            try
            {
                var principal = _jwtService.ValidateToken(token);
                if (principal == null)
                {
                    // 👇 Mensaje exacto que valida tu test Unauthorized
                    return Unauthorized(new { Success = false, Message = "Token inválido o expirado" });
                }

                var claimsDict = principal.Claims.ToDictionary(c => c.Type, c => c.Value);
                return Ok(ServiceResultWrapper<object>.Ok(claimsDict, "Token válido"));
            }
            catch (Exception ex)
            {
                // 👇 Mensaje exacto que esperan tus tests
                return StatusCode(500, new { Success = false, Message = $"Error al validar token: {ex.Message}" });
            }
        }
    }
}
