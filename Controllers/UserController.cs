using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AudioAthleteApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly string _connectionString;

        public UsersController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultDb");
        }

        //--------------------------------------------------//
        //                  GET USERS                       //
        //--------------------------------------------------//
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
                    FROM users
                    LIMIT 10;
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
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                  POST USERS                      //
        //--------------------------------------------------//
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

                int userId;
                int? teamIdToAssign = null;

                //--------------------------------------------------//
                //                     COACH                       //
                //--------------------------------------------------//
                if (newUser.UserType.Equals("coach", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(newUser.Email))
                        return BadRequest(new { error = "Email is required when creating a coach." });

                    if (string.IsNullOrWhiteSpace(newUser.TeamName))
                        return BadRequest(new { error = "Team name is required when creating a coach." });

                    var insertCoachQuery = @"
                        INSERT INTO users (name, username, password, user_type, coach_email, position)
                        VALUES (@name, @username, @password, @userType, @coachEmail, NULL);
                        SELECT LAST_INSERT_ID();
                    ";

                    await using (var cmd = new MySqlCommand(insertCoachQuery, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@name", newUser.Name);
                        cmd.Parameters.AddWithValue("@username", newUser.Username);
                        cmd.Parameters.AddWithValue("@password", newUser.Password);
                        cmd.Parameters.AddWithValue("@userType", newUser.UserType);
                        cmd.Parameters.AddWithValue("@coachEmail", newUser.Email);

                        userId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    }

                    var insertTeamQuery = @"
                        INSERT INTO teams (name, coach_id)
                        VALUES (@teamName, @coachId);
                        SELECT LAST_INSERT_ID();
                    ";

                    await using (var cmd = new MySqlCommand(insertTeamQuery, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@teamName", newUser.TeamName);
                        cmd.Parameters.AddWithValue("@coachId", userId);

                        teamIdToAssign = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    }

                    var updateCoachQuery = @"UPDATE users SET team_id = @teamId WHERE id = @coachId;";

                    await using (var cmd = new MySqlCommand(updateCoachQuery, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@teamId", teamIdToAssign);
                        cmd.Parameters.AddWithValue("@coachId", userId);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                //--------------------------------------------------//
                //                     PLAYER                      //
                //--------------------------------------------------//
                else if (newUser.UserType.Equals("player", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(newUser.Position))
                        return BadRequest(new { error = "Position is required for players." });

                    if (newUser.CoachId is null)
                        return BadRequest(new { error = "CoachId is required when creating a player." });

                    var insertPlayerQuery = @"
                        INSERT INTO users (name, username, password, user_type, position)
                        VALUES (@name, @username, @password, @userType, @position);
                        SELECT LAST_INSERT_ID();
                    ";

                    await using (var cmd = new MySqlCommand(insertPlayerQuery, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@name", newUser.Name);
                        cmd.Parameters.AddWithValue("@username", newUser.Username);
                        cmd.Parameters.AddWithValue("@password", newUser.Password);
                        cmd.Parameters.AddWithValue("@userType", newUser.UserType);
                        cmd.Parameters.AddWithValue("@position", newUser.Position);

                        userId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    }

                    var getCoachTeamQuery = @"
                        SELECT team_id FROM users WHERE id = @coachId AND user_type = 'coach';
                    ";

                    await using (var cmd = new MySqlCommand(getCoachTeamQuery, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@coachId", newUser.CoachId);

                        var result = await cmd.ExecuteScalarAsync();

                        if (result == null || result == DBNull.Value)
                            return BadRequest(new { error = "Invalid CoachId or coach has no team assigned." });

                        teamIdToAssign = Convert.ToInt32(result);
                    }

                    var updatePlayerTeamQuery = @"UPDATE users SET team_id = @teamId WHERE id = @playerId;";

                    await using (var cmd = new MySqlCommand(updatePlayerTeamQuery, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@teamId", teamIdToAssign);
                        cmd.Parameters.AddWithValue("@playerId", userId);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                else
                {
                    return BadRequest(new { error = "Invalid user type. Must be 'coach' or 'player'." });
                }

                await transaction.CommitAsync();

                return Ok(new
                {
                    message = $"{newUser.UserType} created successfully!",
                    user_id = userId,
                    assigned_team_id = teamIdToAssign
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                 DELETE USER                      //
        //--------------------------------------------------//
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
                var rowsAffected = await command.ExecuteNonQueryAsync();

                return rowsAffected > 0
                    ? Ok(new { message = "User deleted successfully!" })
                    : NotFound(new { error = "User not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    //--------------------------------------------------//
    //                    DTO CLASS                     //
    //--------------------------------------------------//
    public class UserDto
    {
        public string Name { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string UserType { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? TeamName { get; set; }
        public int? CoachId { get; set; }
        public string? Position { get; set; }
    }
}
