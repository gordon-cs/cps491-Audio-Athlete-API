using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AudioAthleteApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TeamsController : ControllerBase
    {
        private readonly string _connectionString;

        public TeamsController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultDb");
        }

        private string GenerateJoinCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        //--------------------------------------------------//
        //                  GET ALL TEAMS                   //
        //--------------------------------------------------//
        [HttpGet]
        public async Task<IActionResult> GetTeams()
        {
            var results = new List<object>();

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT t.id, 
                           t.name AS team_name, 
                           t.coach_id, 
                           t.join_code,
                           u.name AS coach_name
                    FROM teams t
                    LEFT JOIN users u ON t.coach_id = u.id;
                ";

                await using var command = new MySqlCommand(query, connection);
                await using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        Id = reader["id"],
                        TeamName = reader["team_name"],
                        CoachId = reader["coach_id"],
                        JoinCode = reader["join_code"] == DBNull.Value ? null : reader["join_code"],
                        CoachName = reader["coach_name"]
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
        //                  GET TEAM BY ID                  //
        //--------------------------------------------------//
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTeamById(int id)
        {
            if (id <= 0) return BadRequest(new { error = "Invalid team ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT t.id, 
                           t.name AS team_name, 
                           t.coach_id, 
                           t.join_code,
                           u.name AS coach_name
                    FROM teams t
                    LEFT JOIN users u ON t.coach_id = u.id
                    WHERE t.id = @id;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);
                await using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return Ok(new
                    {
                        Id = reader["id"],
                        TeamName = reader["team_name"],
                        CoachId = reader["coach_id"],
                        JoinCode = reader["join_code"] == DBNull.Value ? null : reader["join_code"],
                        CoachName = reader["coach_name"]
                    });
                }

                return NotFound(new { error = "Team not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //        ADDED: GET ALL PLAYERS ON A TEAM          //
        //--------------------------------------------------//
        [HttpGet("{id}/players")]
        public async Task<IActionResult> GetPlayersByTeam(int id)
        {
            if (id <= 0) return BadRequest(new { error = "Invalid team ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, name, username, position
                    FROM users
                    WHERE team_id = @teamId AND user_type = 'player';
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@teamId", id);
                await using var reader = await command.ExecuteReaderAsync();

                var players = new List<object>();
                while (await reader.ReadAsync())
                {
                    players.Add(new
                    {
                        Id = reader["id"],
                        Name = reader["name"],
                        Username = reader["username"],
                        Position = reader["position"]
                    });
                }

                return Ok(players);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                  CREATE NEW TEAM                 //
        //--------------------------------------------------//
        [HttpPost]
        public async Task<IActionResult> AddTeam([FromBody] TeamDto newTeam)
        {
            if (string.IsNullOrWhiteSpace(newTeam.Name) || newTeam.CoachId <= 0)
                return BadRequest(new { error = "Team name and valid coach_id are required." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var coachCheckQuery = @"SELECT COUNT(*) FROM users WHERE id = @coachId AND user_type = 'coach';";
                await using (var coachCheck = new MySqlCommand(coachCheckQuery, connection))
                {
                    coachCheck.Parameters.AddWithValue("@coachId", newTeam.CoachId);
                    var count = Convert.ToInt32(await coachCheck.ExecuteScalarAsync());
                    if (count == 0)
                        return BadRequest(new { error = "Coach ID not found or not a coach." });
                }

                var joinCode = GenerateJoinCode();

                var query = @"
                    INSERT INTO teams (coach_id, name, join_code)
                    VALUES (@coachId, @name, @joinCode);
                    SELECT LAST_INSERT_ID();
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@coachId", newTeam.CoachId);
                command.Parameters.AddWithValue("@name", newTeam.Name);
                command.Parameters.AddWithValue("@joinCode", joinCode);

                var newTeamId = Convert.ToInt32(await command.ExecuteScalarAsync());

                return Ok(new
                {
                    message = "Team created successfully!",
                    team_id = newTeamId,
                    join_code = joinCode
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //         ADDED: REGENERATE JOIN CODE              //
        //--------------------------------------------------//
        [HttpPost("{id}/regenerate-code")]
        public async Task<IActionResult> RegenerateJoinCode(int id)
        {
            if (id <= 0) return BadRequest(new { error = "Invalid team ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var newCode = GenerateJoinCode();

                var query = @"UPDATE teams SET join_code = @joinCode WHERE id = @id;";
                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@joinCode", newCode);
                command.Parameters.AddWithValue("@id", id);

                var rows = await command.ExecuteNonQueryAsync();
                if (rows == 0) return NotFound(new { error = "Team not found." });

                return Ok(new { message = "Join code regenerated.", join_code = newCode });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                   DELETE TEAM                    //
        //--------------------------------------------------//
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTeam(int id)
        {
            if (id <= 0)
                return BadRequest(new { error = "Invalid team ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"DELETE FROM teams WHERE id = @id;";
                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                    return Ok(new { message = "Team deleted successfully!" });

                return NotFound(new { error = "Team not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class TeamDto
    {
        public int CoachId { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
