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
        //--------------------------------------------------//
        //                 UPDATE WORKOUT                   //
        //--------------------------------------------------//
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateWorkout(int id, [FromBody] WorkoutUpdateDto updatedWorkout)
        {
            if (id <= 0 || string.IsNullOrWhiteSpace(updatedWorkout.Title))
                return BadRequest(new { error = "Invalid data provided." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    var updateWorkoutQuery = @"
                        UPDATE workouts 
                        SET title = @title, scheduled_date = @date 
                        WHERE id = @id;";
                    
                    await using (var workoutCmd = new MySqlCommand(updateWorkoutQuery, connection, transaction))
                    {
                        workoutCmd.Parameters.AddWithValue("@title", updatedWorkout.Title);
                        workoutCmd.Parameters.AddWithValue("@date", updatedWorkout.ScheduledDate);
                        workoutCmd.Parameters.AddWithValue("@id", id);
                        await workoutCmd.ExecuteNonQueryAsync();
                    }

                    var deletePromptsQuery = "DELETE FROM workout_prompts WHERE workout_id = @id;";
                    await using (var deleteCmd = new MySqlCommand(deletePromptsQuery, connection, transaction))
                    {
                        deleteCmd.Parameters.AddWithValue("@id", id);
                        await deleteCmd.ExecuteNonQueryAsync();
                    }

                    var insertPromptQuery = @"
                        INSERT INTO workout_prompts (workout_id, block_length, instruction) 
                        VALUES (@wId, @len, @instr);";

                    int totalLength = 0;
                    await using (var insertCmd = new MySqlCommand(insertPromptQuery, connection, transaction))
                    {
                        insertCmd.Parameters.Add("@wId", MySqlDbType.Int32).Value = id;
                        insertCmd.Parameters.Add("@len", MySqlDbType.Int32);
                        insertCmd.Parameters.Add("@instr", MySqlDbType.VarChar);

                        foreach (var prompt in updatedWorkout.Prompts)
                        {
                            totalLength += prompt.BlockLength;
                            
                            insertCmd.Parameters["@len"].Value = prompt.BlockLength;
                            insertCmd.Parameters["@instr"].Value = prompt.Instruction;
                            
                            await insertCmd.ExecuteNonQueryAsync();
                        }
                    }

                    var updateLengthQuery = "UPDATE workouts SET total_length_sec = @total WHERE id = @id;";
                    await using (var lengthCmd = new MySqlCommand(updateLengthQuery, connection, transaction))
                    {
                        lengthCmd.Parameters.AddWithValue("@total", totalLength);
                        lengthCmd.Parameters.AddWithValue("@id", id);
                        await lengthCmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return Ok(new { message = "Workout updated successfully!", totalLengthSec = totalLength });
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw; 
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Internal server error.", details = ex.Message });
            }
        }

        //--------------------------------------------------//
        //             POST WORKOUT COMPLETION              //
        //--------------------------------------------------//
        [HttpPost("{id}/complete")]
        public async Task<IActionResult> CompleteWorkout(int id, [FromBody] WorkoutCompletionDto completion)
        {
            if (id <= 0 || completion.PlayerId <= 0)
                return BadRequest(new { error = "Invalid workout ID or player ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    INSERT INTO workout_completions (workout_id, player_id, completed, completed_at)
                    VALUES (@workoutId, @playerId, TRUE, NOW())
                    ON DUPLICATE KEY UPDATE
                        completed = TRUE,
                        completed_at = NOW();
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@workoutId", id);
                command.Parameters.AddWithValue("@playerId", completion.PlayerId);

                await command.ExecuteNonQueryAsync();

                return Ok(new
                {
                    message = "Workout marked as completed!",
                    workoutId = id,
                    playerId = completion.PlayerId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //           GET WORKOUT COMPLETION STATUS          //
        //--------------------------------------------------//
        [HttpGet("{id}/completed/{playerId}")]
        public async Task<IActionResult> GetWorkoutCompletionStatus(int id, int playerId)
        {
            if (id <= 0 || playerId <= 0)
                return BadRequest(new { error = "Invalid workout ID or player ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT completed, completed_at
                    FROM workout_completions
                    WHERE workout_id = @workoutId AND player_id = @playerId
                    LIMIT 1;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@workoutId", id);
                command.Parameters.AddWithValue("@playerId", playerId);

                await using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return Ok(new
                    {
                        workoutId = id,
                        playerId = playerId,
                        completed = Convert.ToBoolean(reader["completed"]),
                        completedAt = reader["completed_at"] == DBNull.Value ? null : reader["completed_at"]
                    });
                }

                return Ok(new
                {
                    workoutId = id,
                    playerId = playerId,
                    completed = false,
                    completedAt = (object?)null
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    //--------------------------------------------------//
    //                    DTO CLASS                      //
    //--------------------------------------------------//
    public class WorkoutDto
    {
        public int? TeamId { get; set; }
        public int? CoachId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime? ScheduledDate { get; set; }
    }

    public class WorkoutUpdateDto
    {
        public string Title { get; set; } = string.Empty;
        public DateTime? ScheduledDate { get; set; }
        public List<WorkoutPromptUpdateDto> Prompts { get; set; } = new();
    }

    public class WorkoutPromptUpdateDto
    {
        public int BlockLength { get; set; }
        public string Instruction { get; set; } = string.Empty;
    }

    public class WorkoutCompletionDto
    {
        public int PlayerId { get; set; }
    }
}