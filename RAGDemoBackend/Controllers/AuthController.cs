using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using RAGDemoBackend.Models;
using RAGDemoBackend.Services;

namespace RAGDemoBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IDevUserStore _userStore;
        private readonly IConfiguration _configuration;

        public AuthController(IDevUserStore userStore, IConfiguration configuration)
        {
            _userStore = userStore;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public ActionResult Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var success = _userStore.TryRegister(request.Username, request.Password);
            if (!success)
            {
                return BadRequest(new { message = "Registration failed" });
            }

            return Ok(new { message = "Registered" });
        }

        [HttpPost("login")]
        public ActionResult<AuthResponse> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!_userStore.ValidateCredentials(request.Username, request.Password, out var role))
            {
                return Unauthorized(new { message = "Invalid credentials" });
            }

            var token = CreateToken(request.Username, role, out var expiresAtUtc);
            return Ok(new AuthResponse
            {
                Token = token,
                ExpiresAtUtc = expiresAtUtc,
                Role = role
            });
        }

        private string CreateToken(string username, string role, out DateTime expiresAtUtc)
        {
            var issuer = _configuration["Jwt:Issuer"] ?? "RAGDemoBackend";
            var audience = _configuration["Jwt:Audience"] ?? "RAGDemoFrontend";
            var key = _configuration["Jwt:Key"] ?? "CHANGE_ME_IN_PRODUCTION";

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, username),
                new Claim("role", role)
            };

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            expiresAtUtc = DateTime.UtcNow.AddHours(8);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiresAtUtc,
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
