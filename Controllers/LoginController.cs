using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
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
        //                      LOGIN                       //
        //--------------------------------------------------//
        [HttpPost]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) ||
                string.IsNullOrWhiteSpace(req.Password))
            {
                return BadRequest(new { error = "Username and password are required." });
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, name, username, password, user_type, team_id
                    FROM users
                    WHERE username = @username
                    LIMIT 1;
                ";

                await using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@username", req.Username);

                await using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Unauthorized(new { error = "Invalid username or password." });
                }

                var storedPassword = reader["password"].ToString();
                if (storedPassword != req.Password)
                {
                    return Unauthorized(new { error = "Invalid username or password." });
                }

                var userId = Convert.ToInt32(reader["id"]);
                var userType = reader["user_type"].ToString();
                var teamId = reader["team_id"] == DBNull.Value ? null : reader["team_id"];

                //--------------------------------------------------//
                //                  JWT CREATION                    //
                //--------------------------------------------------//
                var token = GenerateJwtToken(userId, userType);

                return Ok(new
                {
                    message = "Login successful",
                    token,
                    user = new
                    {
                        id = userId,
                        username = req.Username,
                        userType,
                        teamId
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //              JWT TOKEN GENERATOR                 //
        //--------------------------------------------------//
        private string GenerateJwtToken(int userId, string userType)
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Key"])
            );

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>()
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("role", userType)
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    //--------------------------------------------------//
    //                  LOGIN DTO                       //
    //--------------------------------------------------//
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
