using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using RAGDemoBackend.Models;

namespace RAGDemoBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _configuration;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<ActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var existingUser = await _userManager.FindByNameAsync(request.Username);
            if (existingUser != null)
            {
                return BadRequest(new { message = "Username is already taken" });
            }

            var user = new ApplicationUser
            {
                UserName = request.Username,
                Email = $"{request.Username}@local"
            };

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                return BadRequest(new { message = "Registration failed", errors = createResult.Errors });
            }

            if (!await _roleManager.RoleExistsAsync("user"))
            {
                await _roleManager.CreateAsync(new IdentityRole("user"));
            }

            var requestedRole = string.IsNullOrWhiteSpace(request.Role)
                ? "user"
                : request.Role.Trim().ToLowerInvariant();

            var isAdminRequester = User?.Claims.Any(c => c.Type == "role" && c.Value == "admin") == true;
            var assignRole = isAdminRequester && requestedRole == "admin"
                ? "admin"
                : "user";

            if (assignRole == "admin" && !await _roleManager.RoleExistsAsync("admin"))
            {
                await _roleManager.CreateAsync(new IdentityRole("admin"));
            }

            await _userManager.AddToRoleAsync(user, assignRole);

            return Ok(new { message = "Registered" });
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null)
            {
                return Unauthorized(new { message = "Invalid credentials" });
            }

            var valid = await _userManager.CheckPasswordAsync(user, request.Password);
            if (!valid)
            {
                return Unauthorized(new { message = "Invalid credentials" });
            }

            var roles = await _userManager.GetRolesAsync(user);
            var adminUser = _configuration["Auth:AdminUsername"] ?? "admin";
            if (string.Equals(user.UserName, adminUser, StringComparison.OrdinalIgnoreCase) &&
                !roles.Contains("admin"))
            {
                if (!await _roleManager.RoleExistsAsync("admin"))
                {
                    await _roleManager.CreateAsync(new IdentityRole("admin"));
                }

                await _userManager.AddToRoleAsync(user, "admin");
                roles = await _userManager.GetRolesAsync(user);
            }

            if (roles.Count == 0)
            {
                await _userManager.AddToRoleAsync(user, "user");
                roles = await _userManager.GetRolesAsync(user);
            }
            var token = CreateToken(user.UserName ?? request.Username, roles, out var expiresAtUtc);
            return Ok(new AuthResponse
            {
                Token = token,
                ExpiresAtUtc = expiresAtUtc,
                Role = roles.FirstOrDefault() ?? "user"
            });
        }

        private string CreateToken(string username, IList<string> roles, out DateTime expiresAtUtc)
        {
            var issuer = _configuration["Jwt:Issuer"] ?? "RAGDemoBackend";
            var audience = _configuration["Jwt:Audience"] ?? "RAGDemoFrontend";
            var key = _configuration["Jwt:Key"] ?? "CHANGE_ME_IN_PRODUCTION";

            var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, username)
    };

            if (roles == null || roles.Count == 0)
            {
                claims.Add(new Claim("role", "USER")); // Use uppercase
            }
            else
            {
                foreach (var role in roles)
                {
                    // Normalize to uppercase to match database
                    claims.Add(new Claim("role", role.ToUpperInvariant()));
                }
            }

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

        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult> Me()
        {
            var username = User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                        ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User?.Identity?.Name;

            if (string.IsNullOrWhiteSpace(username))
            {
                return Unauthorized();
            }

            var user = await _userManager.FindByNameAsync(username);
            if (user == null)
            {
                return Unauthorized();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();

            return Ok(new { username, roles, claims });
        }
    }
}
