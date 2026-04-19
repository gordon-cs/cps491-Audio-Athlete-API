using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Isopoh.Cryptography.Argon2;
using System.Text;
using Mailjet.Client;
using Mailjet.Client.Resources;
using Newtonsoft.Json.Linq;

namespace AudioAthleteApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly IConfiguration _config;

        public UsersController(IConfiguration config)
        {
            _config = config;
            _connectionString = config.GetConnectionString("DefaultDb");
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var results = new List<object>();
            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, name, username, password, position, user_type, coach_email, team_id
                    FROM users;
                ";
                await using var command = new MySqlCommand(query, connection);
                await using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        Id = reader["id"],
                        Name = reader["name"],
                        Username = reader["username"],
                        Password = reader["password"],
                        UserType = reader["user_type"],
                        Email = reader["coach_email"] == DBNull.Value ? null : reader["coach_email"],
                        TeamId = reader["team_id"] == DBNull.Value ? null : reader["team_id"],
                        Position = reader["position"]
                    });
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddUser([FromBody] UserDto newUser)
        {
            if (string.IsNullOrWhiteSpace(newUser.Name) ||
                string.IsNullOrWhiteSpace(newUser.Username) ||
                string.IsNullOrWhiteSpace(newUser.Password) ||
                string.IsNullOrWhiteSpace(newUser.UserType))
            {
                return BadRequest(new { error = "Name, Username, Password, and UserType are required." });
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();

                var checkUsername = new MySqlCommand("SELECT COUNT(*) FROM users WHERE username = @username;", connection, transaction);
                checkUsername.Parameters.AddWithValue("@username", newUser.Username);
                var usernameExists = Convert.ToInt32(await checkUsername.ExecuteScalarAsync()) > 0;
                if (usernameExists)
                    return BadRequest(new { error = "Username already exists." });

                if (!string.IsNullOrWhiteSpace(newUser.Email))
                {
                    var checkEmail = new MySqlCommand("SELECT COUNT(*) FROM users WHERE coach_email = @email;", connection, transaction);
                    checkEmail.Parameters.AddWithValue("@email", newUser.Email);
                    var emailExists = Convert.ToInt32(await checkEmail.ExecuteScalarAsync()) > 0;
                    if (emailExists)
                        return BadRequest(new { error = "Email already exists." });
                }

                if (newUser.UserType.Equals("coach", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(newUser.Email))
                        return BadRequest(new { error = "Email is required when creating a coach." });

                    var insertCoachQuery = @"
                        INSERT INTO users (name, username, password, user_type, coach_email, position)
                        VALUES (@name, @username, @password, @userType, @coachEmail, NULL);
                        SELECT LAST_INSERT_ID();
                    ";
                    int userId;
                    await using (var cmd = new MySqlCommand(insertCoachQuery, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@name", newUser.Name);
                        cmd.Parameters.AddWithValue("@username", newUser.Username);
                        cmd.Parameters.AddWithValue("@password", HashPassword(newUser.Password));
                        cmd.Parameters.AddWithValue("@userType", newUser.UserType);
                        cmd.Parameters.AddWithValue("@coachEmail", newUser.Email);

                        userId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    }


                    await transaction.CommitAsync();

                    try
                    {
                        await SendEmailAsync(
                            newUser.Email!,
                            "Welcome to AudioAthlete",
                            $"{newUser.Name},<br/><br/>Your coach account has been created successfully!"
                        );
                    }
                    catch { }

                    return Ok(new
                    {
                        message = "Coach created successfully! Use POST /api/teams to create and manage teams.",
                        user_id = userId
                    });
                }
                else if (newUser.UserType.Equals("player", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(newUser.Position))
                        return BadRequest(new { error = "Position is required for players." });

                    var insertPlayerQuery = @"
                        INSERT INTO users (name, username, password, user_type, position, coach_email)
                        VALUES (@name, @username, @password, @userType, @position, @email);
                        SELECT LAST_INSERT_ID();
                    ";
                    int userId;
                    await using (var cmd = new MySqlCommand(insertPlayerQuery, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@name", newUser.Name);
                        cmd.Parameters.AddWithValue("@username", newUser.Username);
                        cmd.Parameters.AddWithValue("@password", HashPassword(newUser.Password));
                        cmd.Parameters.AddWithValue("@userType", newUser.UserType);
                        cmd.Parameters.AddWithValue("@position", newUser.Position);
                        cmd.Parameters.AddWithValue("@email", newUser.Email ?? (object)DBNull.Value);
                        userId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    }

                    await transaction.CommitAsync();

                    return Ok(new
                    {
                        message = "Player created successfully! Use POST /api/users/join with a team code to join a team.",
                        user_id = userId
                    });
                }

                return BadRequest(new { error = "Invalid user type." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("join")]
        public async Task<IActionResult> JoinTeam([FromBody] JoinTeamDto joinRequest)
        {
            if (string.IsNullOrWhiteSpace(joinRequest.Code) || joinRequest.UserId <= 0)
                return BadRequest(new { error = "User ID and Team Code are required." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var findTeamQuery = "SELECT id FROM teams WHERE join_code = @code;";
                int teamId;
                await using (var cmd = new MySqlCommand(findTeamQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@code", joinRequest.Code.ToUpper().Trim());
                    var result = await cmd.ExecuteScalarAsync();
                    if (result == null || result == DBNull.Value)
                        return NotFound(new { error = "Invalid team code." });
                    teamId = Convert.ToInt32(result);
                }

                var updateQuery = "UPDATE users SET team_id = @teamId WHERE id = @userId AND user_type = 'player';";
                await using (var cmd = new MySqlCommand(updateQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@teamId", teamId);
                    cmd.Parameters.AddWithValue("@userId", joinRequest.UserId);
                    var affected = await cmd.ExecuteNonQueryAsync();
                    if (affected == 0) return BadRequest(new { error = "Update failed. Verify user is a player." });
                }

                return Ok(new { message = "Joined team successfully!", team_id = teamId });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest(new { error = "Email is required." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var findUser = @"SELECT id FROM users WHERE coach_email = @Email OR username = @Email;";
                await using var cmd = new MySqlCommand(findUser, connection);
                cmd.Parameters.AddWithValue("@Email", dto.Email);

                var result = await cmd.ExecuteScalarAsync();
                if (result == null)
                    return Ok(new { message = "If this email exists, a reset link has been sent." });

                var userId = Convert.ToInt32(result);
                var token = Guid.NewGuid().ToString("N");
                var expiry = DateTime.UtcNow.AddMinutes(30);

                var update = @"UPDATE users SET reset_token = @Token, reset_token_expiry = @Expiry WHERE id = @Id;";
                await using var updateCmd = new MySqlCommand(update, connection);
                updateCmd.Parameters.AddWithValue("@Token", token);
                updateCmd.Parameters.AddWithValue("@Expiry", expiry);
                updateCmd.Parameters.AddWithValue("@Id", userId);
                await updateCmd.ExecuteNonQueryAsync();

                var resetUrl = $"http://localhost:8081/main/resetPassword?token={token}";

                await SendEmailAsync(
                    dto.Email,
                    "Reset your AudioAthlete password",
                    $"Click to reset your password:<br/><a href=\"{resetUrl}\">Reset Password</a>"
                );

                return Ok(new { message = "Reset link sent." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest(new { error = "Token and new password required." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"SELECT id, reset_token_expiry FROM users WHERE reset_token = @Token;";
                await using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@Token", dto.Token);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return BadRequest(new { error = "Invalid token." });

                var expiry = reader.GetDateTime("reset_token_expiry");
                var userId = reader.GetInt32("id");

                if (expiry < DateTime.UtcNow)
                    return BadRequest(new { error = "Token expired." });

                reader.Close();

                var newHash = HashPassword(dto.NewPassword);

                var update = @"UPDATE users 
                               SET password = @Password, reset_token = NULL, reset_token_expiry = NULL
                               WHERE id = @Id;";
                await using var updateCmd = new MySqlCommand(update, connection);
                updateCmd.Parameters.AddWithValue("@Password", newHash);
                updateCmd.Parameters.AddWithValue("@Id", userId);
                await updateCmd.ExecuteNonQueryAsync();

                return Ok(new { message = "Password updated." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            if (id <= 0)
                return BadRequest(new { error = "Invalid user ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"DELETE FROM users WHERE id = @id;";
                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);
                var rows = await command.ExecuteNonQueryAsync();

                return rows > 0
                    ? Ok(new { message = "User deleted successfully!" })
                    : NotFound(new { error = "User not found." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private string HashPassword(string password)
        {
            byte[] salt = new byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(salt);

            var config = new Argon2Config
            {
                Type = Argon2Type.DataIndependentAddressing,
                TimeCost = 4,
                MemoryCost = 1024 * 64,
                Lanes = 4,
                Threads = 4,
                Salt = salt,
                Password = Encoding.UTF8.GetBytes(password)
            };

            using var argon2 = new Argon2(config);
            var hash = argon2.Hash();

            return config.EncodeString(hash.Buffer);
        }

        private async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var apiKey = _config["Email:MailjetApiKey"];
            var secretKey = _config["Email:MailjetSecretKey"];
            var fromEmail = _config["Email:FromEmail"];
            var fromName = _config["Email:FromName"] ?? "Audio Athlete";

            var client = new MailjetClient(apiKey, secretKey);

            var request = new MailjetRequest
            {
                Resource = Send.Resource
            }
            .Property(Send.Messages, new JArray
            {
                new JObject
                {
                    ["FromEmail"] = fromEmail,
                    ["FromName"] = fromName,
                    ["Subject"] = subject,
                    ["Text-Part"] = body,
                    ["Html-Part"] = body,
                    ["Recipients"] = new JArray { new JObject { ["Email"] = toEmail } }
                }
            });

            await client.PostAsync(request);
        }
    }

    public class UserDto
    {
        public string Name { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string UserType { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Position { get; set; }
    }

    public class ForgotPasswordDto
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordDto
    {
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class JoinTeamDto
    {
        public int UserId { get; set; }
        public string Code { get; set; } = string.Empty;
    }
}
