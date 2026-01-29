using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AudioAthleteApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SavedWorkoutsController : ControllerBase
    {
        private readonly string _connectionString;

        public SavedWorkoutsController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultDb");
        }

        // --------------------------------------------------//
        //                GET SAVED WORKOUTS                 //
        // --------------------------------------------------//
        [HttpGet]
        public async Task<IActionResult> GetSavedWorkouts([FromQuery] int? teamId, [FromQuery] int? coachId)
        {
            var results = new List<object>();

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, team_id, coach_id, title, total_length_sec
                    FROM saved_workouts
                    WHERE (@teamId IS NULL OR team_id = @teamId)
                      AND (@coachId IS NULL OR coach_id = @coachId)
                    ORDER BY id DESC;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@teamId", (object?)teamId ?? DBNull.Value);
                command.Parameters.AddWithValue("@coachId", (object?)coachId ?? DBNull.Value);

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

        // --------------------------------------------------//
        //                POST SAVED WORKOUT                 //
        // --------------------------------------------------//
        [HttpPost]
        public async Task<IActionResult> AddSavedWorkout([FromBody] SavedWorkoutDto dto)
        {
            if (dto.TeamId == null || dto.CoachId == null || string.IsNullOrWhiteSpace(dto.Title))
                return BadRequest(new { error = "TeamId, CoachId, and Title are required." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    INSERT INTO saved_workouts (team_id, coach_id, title, total_length_sec)
                    VALUES (@teamId, @coachId, @title, @totalLengthSec);
                    SELECT LAST_INSERT_ID();
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@teamId", dto.TeamId);
                command.Parameters.AddWithValue("@coachId", dto.CoachId);
                command.Parameters.AddWithValue("@title", dto.Title);
                command.Parameters.AddWithValue("@totalLengthSec", dto.TotalLengthSec ?? 0);

                var savedWorkoutId = Convert.ToInt32(await command.ExecuteScalarAsync());

                return Ok(new { message = "Saved workout created!", saved_workout_id = savedWorkoutId });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // --------------------------------------------------//
        //               DELETE SAVED WORKOUT                //
        // --------------------------------------------------//
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSavedWorkout(int id)
        {
            if (id <= 0) return BadRequest(new { error = "Invalid saved workout ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"DELETE FROM saved_workouts WHERE id = @id;";
                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);

                var rows = await command.ExecuteNonQueryAsync();
                if (rows > 0) return Ok(new { message = "Saved workout deleted!" });
                return NotFound(new { error = "Saved workout not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // --------------------------------------------------//
        //      POST: SAVE AN EXISTING WORKOUT AS TEMPLATE   //
        //      /api/savedworkouts/from-workout/{workoutId}  //
        // --------------------------------------------------//
        [HttpPost("from-workout/{workoutId}")]
        public async Task<IActionResult> SaveFromWorkout(int workoutId)
        {
            if (workoutId <= 0) return BadRequest(new { error = "Invalid workout ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var tx = await connection.BeginTransactionAsync();

                var insertSavedWorkout = @"
                    INSERT INTO saved_workouts (team_id, coach_id, title, total_length_sec)
                    SELECT team_id, coach_id, title, total_length_sec
                    FROM workouts
                    WHERE id = @workoutId;

                    SELECT LAST_INSERT_ID();
                ";

                int savedWorkoutId;
                await using (var cmd = new MySqlCommand(insertSavedWorkout, connection, (MySqlTransaction)tx))
                {
                    cmd.Parameters.AddWithValue("@workoutId", workoutId);
                    savedWorkoutId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                if (savedWorkoutId <= 0)
                {
                    await tx.RollbackAsync();
                    return NotFound(new { error = "Workout not found." });
                }

                // NOTE: This assumes you already created saved_workout_prompts table.
                // sort_order: using wp.id as a stable increasing order (works if wp.id increases with insertion)
                var copyPrompts = @"
                    INSERT INTO saved_workout_prompts (saved_workout_id, sort_order, block_length, instruction)
                    SELECT @savedId, wp.id, wp.block_length, wp.instruction
                    FROM workout_prompts wp
                    WHERE wp.workout_id = @workoutId
                    ORDER BY wp.id;
                ";

                await using (var cmd = new MySqlCommand(copyPrompts, connection, (MySqlTransaction)tx))
                {
                    cmd.Parameters.AddWithValue("@savedId", savedWorkoutId);
                    cmd.Parameters.AddWithValue("@workoutId", workoutId);
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                return Ok(new { message = "Workout saved as template!", saved_workout_id = savedWorkoutId });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // --------------------------------------------------//
        //   POST: SCHEDULE A WORKOUT FROM A SAVED TEMPLATE  //
        //   /api/savedworkouts/{id}/schedule                //
        // --------------------------------------------------//
        [HttpPost("{id}/schedule")]
        public async Task<IActionResult> ScheduleFromSavedWorkout(int id, [FromBody] ScheduleFromSavedDto dto)
        {
            if (id <= 0) return BadRequest(new { error = "Invalid saved workout ID." });
            if (dto.ScheduledDate == null) return BadRequest(new { error = "ScheduledDate is required." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var tx = await connection.BeginTransactionAsync();

                var insertWorkout = @"
                    INSERT INTO workouts (team_id, coach_id, title, total_length_sec, scheduled_date)
                    SELECT team_id, coach_id, title, total_length_sec, @scheduledDate
                    FROM saved_workouts
                    WHERE id = @savedWorkoutId;

                    SELECT LAST_INSERT_ID();
                ";

                int newWorkoutId;
                await using (var cmd = new MySqlCommand(insertWorkout, connection, (MySqlTransaction)tx))
                {
                    cmd.Parameters.AddWithValue("@savedWorkoutId", id);
                    cmd.Parameters.AddWithValue("@scheduledDate", dto.ScheduledDate);
                    newWorkoutId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                if (newWorkoutId <= 0)
                {
                    await tx.RollbackAsync();
                    return NotFound(new { error = "Saved workout not found." });
                }

                var copyPrompts = @"
                    INSERT INTO workout_prompts (workout_id, block_length, instruction)
                    SELECT @newWorkoutId, swp.block_length, swp.instruction
                    FROM saved_workout_prompts swp
                    WHERE swp.saved_workout_id = @savedWorkoutId
                    ORDER BY swp.sort_order;
                ";

                await using (var cmd = new MySqlCommand(copyPrompts, connection, (MySqlTransaction)tx))
                {
                    cmd.Parameters.AddWithValue("@newWorkoutId", newWorkoutId);
                    cmd.Parameters.AddWithValue("@savedWorkoutId", id);
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                return Ok(new { message = "Workout scheduled from template!", workout_id = newWorkoutId });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class SavedWorkoutDto
    {
        public int? TeamId { get; set; }
        public int? CoachId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int? TotalLengthSec { get; set; }
    }

    public class ScheduleFromSavedDto
    {
        public DateTime? ScheduledDate { get; set; }
    }
}
