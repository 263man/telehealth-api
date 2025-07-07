using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace TelehealthApi.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            IConfiguration configuration,
            ILogger<AccountController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Registers a new user with the system.
        /// </summary>
        /// <param name="model">User registration details.</param>
        /// <returns>A status indicating success or failure of registration.</returns>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterModel model)
        {
            // Detailed logging (as per our discussion)
            _logger.LogInformation("Attempting to register user with email: {Email}, FirstName: {FirstName}, LastName: {LastName}",
                                    model.Email, model.FirstName, model.LastName);

            if (ModelState.IsValid == false) // Check if the incoming model data is invalid
            {
                // Log a warning message, including the email and validation errors
                _logger.LogWarning("Register model state invalid for email: {Email}. Errors: {Errors}",
                                    model.Email,
                                    ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));

                // Return a BadRequest response, sending the model validation errors back to the client
                return BadRequest(ModelState);
            }

            var userExists = await _userManager.FindByEmailAsync(model.Email);
            if (userExists != null)
            {
                _logger.LogWarning("Registration failed: User with email {Email} already exists.", model.Email);
                return StatusCode(StatusCodes.Status409Conflict, "User with this email already exists.");
            }

            IdentityUser user = new()
            {
                Email = model.Email,
                SecurityStamp = Guid.NewGuid().ToString(),
                UserName = model.Email // Using email as username for simplicity, can be changed
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded == false)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogError("User registration failed for email {Email}. Errors: {Errors}", model.Email, errors);
                return StatusCode(StatusCodes.Status500InternalServerError, $"User creation failed! Errors: {errors}");
            }

            _logger.LogInformation("User {Email} registered successfully.", model.Email);
            return Ok("User registered successfully!");
        }

        /// <summary>
        /// Authenticates a user and returns a JWT token.
        /// </summary>
        /// <param name="model">User login credentials.</param>
        /// <returns>A JWT token if authentication is successful.</returns>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            _logger.LogInformation("Attempting to log in user with email: {Email}", model.Email);

            if (ModelState.IsValid == false)
            {
                _logger.LogWarning("Login model state invalid for email: {Email}. Errors: {Errors}",
                                    model.Email, ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return BadRequest(ModelState);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                _logger.LogWarning("Login failed: User with email {Email} not found.", model.Email);
                return Unauthorized("Invalid credentials.");
            }

            var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, false); // false for lockoutOnFailure

            if (result.Succeeded == false)
            {
                _logger.LogWarning("Login failed: Invalid password for user {Email}.", model.Email);
                return Unauthorized("Invalid credentials.");
            }

            // If authentication is successful, generate JWT token
            var token = GenerateJwtToken(user);
            _logger.LogInformation("User {Email} logged in successfully. Token generated.", model.Email);
            return Ok(new { Token = token });
        }

        private string GenerateJwtToken(IdentityUser user)
        {
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty), // Ensure UserId is available in claims
                new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty) // Use UserName if available, otherwise Email, otherwise empty string
            };
            // Add SecurityStamp claim for proper ASP.NET Core Identity validation.
            // This is crucial for invalidating tokens if user details (like password or roles) change.
            if (!string.IsNullOrEmpty(user.SecurityStamp))
            {
                // The default claim type for SecurityStamp is "AspNet.Identity.SecurityStamp"
                claims.Add(new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp));
            }

            // Add roles to claims (if user has roles)
            var userRoles = _userManager.GetRolesAsync(user).Result; // Synchronous for simplicity, can be awaited
            foreach (var role in userRoles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is not configured.")));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.Now.AddDays(Convert.ToDouble(_configuration["Jwt:ExpireDays"] ?? "7"));

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
    // Input model for user registration
    public class RegisterModel
    {
        [Required]
        [EmailAddress]
        public required string Email { get; set; }

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        public required string Password { get; set; }

        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public required string ConfirmPassword { get; set; }

        [Required]
        public required string FirstName { get; set; }

        [Required]
        public required string LastName { get; set; }
    }

    // Input model for user login
    public class LoginModel
    {
        [Required]
        [EmailAddress]
        public required string Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public required string Password { get; set; }
    }
}
