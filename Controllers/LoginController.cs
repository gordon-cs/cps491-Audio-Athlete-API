using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using MySqlConnector;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AudioAthleteApi.Controllers 
{
    [ApiController]
    [Route("api/[controller]")]
    public class LoginController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly IConfiguration _config;

        public LoginController(IConfiguration config)
        {
            _config = config;
            _connectionString = config.GetConnectionString("DefaultDb");
        }

        //--------------------------------------------------//
        //                     LOGIN                        //
        //--------------------------------------------------//
        [HttpPost]
        public async Task<IActionResult> Login([FromBody] LoginDto credentials)
        {
            if (string.IsNullOrWhiteSpace(credentials.Username) || string.IsNullOrWhiteSpace(credentials.Password))
            {
                return BadRequest(new { error = "Username and password are required." });
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, name, username, user_type, coach_email, team_id
                    FROM users
                    WHERE username = @username AND password = @password
                    LIMIT 1;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@username", credentials.Username);
                command.Parameters.AddWithValue("@password", credentials.Password);

                await using var reader = await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Unauthorized(new { error = "Invalid username or password." });
                }

                var userId = Convert.ToInt32(reader["id"]);
                var username = reader["username"].ToString();


                // Generate JWT
                var token = GenerateJwtToken(userId, username);

                var user = new
                {
                    Id = userId,
                    Name = reader["name"],
                    Username = username,
                    UserType = reader["user_type"],
                    Email = reader["coach_email"] == DBNull.Value ? null : reader["coach_email"],
                    TeamId = reader["team_id"] == DBNull.Value ? null : reader["team_id"]
                };

                return Ok(new
                {
                    message = "Login successful!",
                    token,
                    user
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                JWT GENERATION                   //
        //--------------------------------------------------//
        private string GenerateJwtToken(int userId, string username)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    //--------------------------------------------------//
    //                    DTO CLASS                     //
    //--------------------------------------------------//
    public class LoginDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
