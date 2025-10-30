using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AudioAthleteApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkoutsController : ControllerBase
    {
        private readonly string _connectionString;

        public WorkoutsController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultDb");
        }

        //--------------------------------------------------//
        //                  GET WORKOUTS                    //
        //--------------------------------------------------//
        [HttpGet]
        public async Task<IActionResult> GetWorkouts()
        {
            var results = new List<object>();

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, team_id, coach_id, title, total_length_sec, scheduled_date
                    FROM workouts
                    ORDER BY scheduled_date DESC;
                ";

                await using var command = new MySqlCommand(query, connection);
                await using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        Id = reader["id"],
                        TeamId = reader["team_id"],
                        CoachId = reader["coach_id"],
                        Title = reader["title"],
                        TotalLengthSec = reader["total_length_sec"],
                        ScheduledDate = reader["scheduled_date"]
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
        //                 POST WORKOUT                     //
        //--------------------------------------------------//
        [HttpPost]
        public async Task<IActionResult> AddWorkout([FromBody] WorkoutDto newWorkout)
        {
            if (newWorkout.TeamId == null ||
                newWorkout.CoachId == null ||
                string.IsNullOrWhiteSpace(newWorkout.Title) ||
                newWorkout.ScheduledDate == null)
            {
                return BadRequest(new { error = "Missing or invalid required fields." });
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // include total_length_sec with default 0
                var query = @"
                    INSERT INTO workouts (team_id, coach_id, title, total_length_sec, scheduled_date)
                    VALUES (@teamId, @coachId, @title, 0, @scheduledDate);
                    SELECT LAST_INSERT_ID();
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@teamId", newWorkout.TeamId);
                command.Parameters.AddWithValue("@coachId", newWorkout.CoachId);
                command.Parameters.AddWithValue("@title", newWorkout.Title);
                command.Parameters.AddWithValue("@scheduledDate", newWorkout.ScheduledDate);

                var workoutId = Convert.ToInt32(await command.ExecuteScalarAsync());

                return Ok(new
                {
                    message = "Workout created successfully!",
                    workout_id = workoutId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                 DELETE WORKOUT                   //
        //--------------------------------------------------//
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWorkout(int id)
        {
            if (id <= 0)
                return BadRequest(new { error = "Invalid workout ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"DELETE FROM workouts WHERE id = @id;";
                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Ok(new { message = "Workout deleted successfully!" });
                }
                else
                {
                    return NotFound(new { error = "Workout not found." });
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
    public class WorkoutDto
    {
        public int? TeamId { get; set; }
        public int? CoachId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime? ScheduledDate { get; set; }
    }
}
