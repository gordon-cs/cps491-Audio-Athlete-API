using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AivenApi.Controllers
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

        //--------------------------------------------------//
        //                  GET TEAMS                       //
        //--------------------------------------------------//
        [HttpGet]
        public async Task<IActionResult> GetTeams()
        {
            var results = new List<object>();

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // Join teams with users to get the coach’s name
                var query = @"
                    SELECT t.id, 
                           t.name AS team_name, 
                           t.coach_id, 
                           u.name AS coach_name
                    FROM teams t
                    LEFT JOIN users u ON t.coach_id = u.id
                    LIMIT 25;
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
        //                  GET BY ID                       //
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
                    var result = new
                    {
                        Id = reader["id"],
                        TeamName = reader["team_name"],
                        CoachId = reader["coach_id"],
                        CoachName = reader["coach_name"]
                    };

                    return Ok(result);
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
        //                  POST TEAM                       //
        //--------------------------------------------------//
        [HttpPost]
        public async Task<IActionResult> AddTeam([FromBody] TeamDto newTeam)
        {
            if (string.IsNullOrWhiteSpace(newTeam.Name) || newTeam.CoachId <= 0)
            {
                return BadRequest(new { error = "Team name and valid coach_id are required." });
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // Validate coach exists and is a coach
                var coachCheckQuery = @"SELECT COUNT(*) FROM users WHERE id = @coachId AND user_type = 'coach';";
                await using (var coachCheck = new MySqlCommand(coachCheckQuery, connection))
                {
                    coachCheck.Parameters.AddWithValue("@coachId", newTeam.CoachId);
                    var count = Convert.ToInt32(await coachCheck.ExecuteScalarAsync());
                    if (count == 0)
                        return BadRequest(new { error = "Coach ID not found or not a coach." });
                }

                var query = @"
                    INSERT INTO teams (coach_id, name)
                    VALUES (@coachId, @name);
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@coachId", newTeam.CoachId);
                command.Parameters.AddWithValue("@name", newTeam.Name);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                    return Ok(new { message = "Team added successfully!" });

                return StatusCode(500, new { error = "Failed to add team." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                 DELETE TEAM                      //
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

    //--------------------------------------------------//
    //                  TEAM DTO                        //
    //--------------------------------------------------//
    public class TeamDto
    {
        public int CoachId { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
