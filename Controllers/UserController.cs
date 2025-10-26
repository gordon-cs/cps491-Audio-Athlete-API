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
                    SELECT id, name, password, user_type, team_id
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
                        Password = reader["password"],
                        UserType = reader["user_type"],
                        TeamId = reader["team_id"] == DBNull.Value ? null : reader["team_id"]
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
                string.IsNullOrWhiteSpace(newUser.Password) ||
                string.IsNullOrWhiteSpace(newUser.UserType))
            {
                return BadRequest(new { error = "Name, Password, and UserType are required." });
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();

                int userId;
                int? teamIdToAssign = null;

                var insertUserQuery = @"
                    INSERT INTO users (name, password, user_type)
                    VALUES (@name, @password, @userType);
                    SELECT LAST_INSERT_ID();
                ";

                await using (var insertUserCmd = new MySqlCommand(insertUserQuery, connection, transaction))
                {
                    insertUserCmd.Parameters.AddWithValue("@name", newUser.Name);
                    insertUserCmd.Parameters.AddWithValue("@password", newUser.Password);
                    insertUserCmd.Parameters.AddWithValue("@userType", newUser.UserType);
                    userId = Convert.ToInt32(await insertUserCmd.ExecuteScalarAsync());
                }

                //--------------------------------------------------//
                //                COACH CREATION FLOW                //
                //--------------------------------------------------//
                if (newUser.UserType.Equals("coach", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(newUser.TeamName))
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { error = "Team name is required when creating a coach." });
                    }

                    var insertTeamQuery = @"
                        INSERT INTO teams (name, coach_id)
                        VALUES (@teamName, @coachId);
                        SELECT LAST_INSERT_ID();
                    ";

                    await using (var insertTeamCmd = new MySqlCommand(insertTeamQuery, connection, transaction))
                    {
                        insertTeamCmd.Parameters.AddWithValue("@teamName", newUser.TeamName);
                        insertTeamCmd.Parameters.AddWithValue("@coachId", userId);
                        teamIdToAssign = Convert.ToInt32(await insertTeamCmd.ExecuteScalarAsync());
                    }

                    var updateCoachQuery = @"UPDATE users SET team_id = @teamId WHERE id = @coachId;";
                    await using (var updateCoachCmd = new MySqlCommand(updateCoachQuery, connection, transaction))
                    {
                        updateCoachCmd.Parameters.AddWithValue("@teamId", teamIdToAssign);
                        updateCoachCmd.Parameters.AddWithValue("@coachId", userId);
                        await updateCoachCmd.ExecuteNonQueryAsync();
                    }
                }

                //--------------------------------------------------//
                //                PLAYER CREATION FLOW               //
                //--------------------------------------------------//
                else if (newUser.UserType.Equals("player", StringComparison.OrdinalIgnoreCase))
                {
                    if (newUser.CoachId == null)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { error = "CoachId is required when creating a player." });
                    }

                    var getCoachTeamQuery = @"
                        SELECT team_id FROM users WHERE id = @coachId AND user_type = 'coach';
                    ";

                    await using (var getCoachTeamCmd = new MySqlCommand(getCoachTeamQuery, connection, transaction))
                    {
                        getCoachTeamCmd.Parameters.AddWithValue("@coachId", newUser.CoachId);
                        var result = await getCoachTeamCmd.ExecuteScalarAsync();

                        if (result == null || result == DBNull.Value)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { error = "Invalid CoachId or coach has no team assigned." });
                        }

                        teamIdToAssign = Convert.ToInt32(result);
                    }

                    var updatePlayerTeamQuery = @"UPDATE users SET team_id = @teamId WHERE id = @playerId;";
                    await using (var updatePlayerCmd = new MySqlCommand(updatePlayerTeamQuery, connection, transaction))
                    {
                        updatePlayerCmd.Parameters.AddWithValue("@teamId", teamIdToAssign);
                        updatePlayerCmd.Parameters.AddWithValue("@playerId", userId);
                        await updatePlayerCmd.ExecuteNonQueryAsync();
                    }

                    var insertTeamPlayerQuery = @"
                        INSERT INTO team_players (team_id, player_id)
                        VALUES (@teamId, @playerId);
                    ";

                    await using (var insertTeamPlayerCmd = new MySqlCommand(insertTeamPlayerQuery, connection, transaction))
                    {
                        insertTeamPlayerCmd.Parameters.AddWithValue("@teamId", teamIdToAssign);
                        insertTeamPlayerCmd.Parameters.AddWithValue("@playerId", userId);
                        await insertTeamPlayerCmd.ExecuteNonQueryAsync();
                    }
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
            {
                return BadRequest(new { error = "Invalid user ID." });
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"DELETE FROM users WHERE id = @id;";
                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Ok(new { message = "User deleted successfully!" });
                }
                else
                {
                    return NotFound(new { error = "User not found." });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    //--------------------------------------------------//
    //                    DTO CLASS                    //
    //--------------------------------------------------//
    public class UserDto
    {
        public string Name { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string UserType { get; set; } = string.Empty;
        public int? CoachId { get; set; } 
        public string? TeamName { get; set; } 
    }
}
