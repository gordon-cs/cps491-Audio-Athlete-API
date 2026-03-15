using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AudioAthleteApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkoutProgramsController : ControllerBase
    {
        private readonly string _connectionString;

        public WorkoutProgramsController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultDb");
        }

        //--------------------------------------------------//
        //               GET ALL PROGRAMS                   //
        //--------------------------------------------------//
        [HttpGet]
        public async Task<IActionResult> GetWorkoutPrograms()
        {
            var results = new List<object>();

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, team_id, coach_id, title, description, created_at
                    FROM workout_programs
                    ORDER BY created_at DESC;
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
                        Description = reader["description"],
                        CreatedAt = reader["created_at"]
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
        //                 GET ONE PROGRAM                  //
        //--------------------------------------------------//
        [HttpGet("{id}")]
        public async Task<IActionResult> GetWorkoutProgram(int id)
        {
            if (id <= 0)
                return BadRequest(new { error = "Invalid program ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, team_id, coach_id, title, description, created_at
                    FROM workout_programs
                    WHERE id = @id
                    LIMIT 1;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);

                await using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return Ok(new
                    {
                        Id = reader["id"],
                        TeamId = reader["team_id"],
                        CoachId = reader["coach_id"],
                        Title = reader["title"],
                        Description = reader["description"],
                        CreatedAt = reader["created_at"]
                    });
                }

                return NotFound(new { error = "Program not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                 POST PROGRAM                     //
        //--------------------------------------------------//
        [HttpPost]
        public async Task<IActionResult> AddWorkoutProgram([FromBody] WorkoutProgramDto newProgram)
        {
            if (newProgram.TeamId == null ||
                newProgram.CoachId == null ||
                string.IsNullOrWhiteSpace(newProgram.Title))
            {
                return BadRequest(new { error = "Missing or invalid required fields." });
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    INSERT INTO workout_programs (team_id, coach_id, title, description)
                    VALUES (@teamId, @coachId, @title, @description);
                    SELECT LAST_INSERT_ID();
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@teamId", newProgram.TeamId);
                command.Parameters.AddWithValue("@coachId", newProgram.CoachId);
                command.Parameters.AddWithValue("@title", newProgram.Title);
                command.Parameters.AddWithValue("@description", (object?)newProgram.Description ?? DBNull.Value);

                var programId = Convert.ToInt32(await command.ExecuteScalarAsync());

                return Ok(new
                {
                    message = "Workout program created successfully!",
                    program_id = programId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                UPDATE PROGRAM                    //
        //--------------------------------------------------//
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateWorkoutProgram(int id, [FromBody] WorkoutProgramUpdateDto updatedProgram)
        {
            if (id <= 0 || string.IsNullOrWhiteSpace(updatedProgram.Title))
                return BadRequest(new { error = "Invalid data provided." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE workout_programs
                    SET title = @title,
                        description = @description
                    WHERE id = @id;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@title", updatedProgram.Title);
                command.Parameters.AddWithValue("@description", (object?)updatedProgram.Description ?? DBNull.Value);
                command.Parameters.AddWithValue("@id", id);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Ok(new { message = "Workout program updated successfully!" });
                }

                return NotFound(new { error = "Program not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                DELETE PROGRAM                    //
        //--------------------------------------------------//
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWorkoutProgram(int id)
        {
            if (id <= 0)
                return BadRequest(new { error = "Invalid program ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"DELETE FROM workout_programs WHERE id = @id;";
                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Ok(new { message = "Workout program deleted successfully!" });
                }

                return NotFound(new { error = "Program not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //           GET WORKOUTS IN PROGRAM                //
        //--------------------------------------------------//
        [HttpGet("{id}/workouts")]
        public async Task<IActionResult> GetProgramWorkouts(int id)
        {
            if (id <= 0)
                return BadRequest(new { error = "Invalid program ID." });

            var results = new List<object>();

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        wpi.id AS program_item_id,
                        wpi.program_id,
                        wpi.workout_id,
                        wpi.sort_order,
                        w.title,
                        w.team_id,
                        w.coach_id,
                        w.total_length_sec,
                        w.scheduled_date
                    FROM workout_program_items wpi
                    JOIN workouts w
                        ON w.id = wpi.workout_id
                    WHERE wpi.program_id = @programId
                    ORDER BY wpi.sort_order ASC, w.scheduled_date ASC;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@programId", id);

                await using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        ProgramItemId = reader["program_item_id"],
                        ProgramId = reader["program_id"],
                        WorkoutId = reader["workout_id"],
                        SortOrder = reader["sort_order"],
                        Title = reader["title"],
                        TeamId = reader["team_id"],
                        CoachId = reader["coach_id"],
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
        //          ADD WORKOUT TO PROGRAM                  //
        //--------------------------------------------------//
        [HttpPost("{id}/workouts")]
        public async Task<IActionResult> AddWorkoutToProgram(int id, [FromBody] ProgramWorkoutItemDto item)
        {
            if (id <= 0 || item.WorkoutId <= 0)
                return BadRequest(new { error = "Invalid program ID or workout ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    INSERT INTO workout_program_items (program_id, workout_id, sort_order)
                    VALUES (@programId, @workoutId, @sortOrder);
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@programId", id);
                command.Parameters.AddWithValue("@workoutId", item.WorkoutId);
                command.Parameters.AddWithValue("@sortOrder", item.SortOrder ?? 0);

                await command.ExecuteNonQueryAsync();

                return Ok(new
                {
                    message = "Workout added to program successfully!",
                    programId = id,
                    workoutId = item.WorkoutId
                });
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return Conflict(new { error = "That workout is already in this program." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //        REMOVE WORKOUT FROM PROGRAM               //
        //--------------------------------------------------//
        [HttpDelete("{id}/workouts/{workoutId}")]
        public async Task<IActionResult> RemoveWorkoutFromProgram(int id, int workoutId)
        {
            if (id <= 0 || workoutId <= 0)
                return BadRequest(new { error = "Invalid program ID or workout ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    DELETE FROM workout_program_items
                    WHERE program_id = @programId AND workout_id = @workoutId;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@programId", id);
                command.Parameters.AddWithValue("@workoutId", workoutId);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Ok(new { message = "Workout removed from program successfully!" });
                }

                return NotFound(new { error = "Workout was not found in that program." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //      UPDATE PROGRAM WORKOUT SORT ORDER           //
        //--------------------------------------------------//
        [HttpPut("{id}/workouts/{workoutId}")]
        public async Task<IActionResult> UpdateProgramWorkout(int id, int workoutId, [FromBody] ProgramWorkoutUpdateDto updatedItem)
        {
            if (id <= 0 || workoutId <= 0)
                return BadRequest(new { error = "Invalid program ID or workout ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE workout_program_items
                    SET sort_order = @sortOrder
                    WHERE program_id = @programId AND workout_id = @workoutId;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@sortOrder", updatedItem.SortOrder);
                command.Parameters.AddWithValue("@programId", id);
                command.Parameters.AddWithValue("@workoutId", workoutId);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Ok(new { message = "Program workout updated successfully!" });
                }

                return NotFound(new { error = "Workout was not found in that program." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //        GET PLAYER PROGRESS FOR PROGRAM           //
        //--------------------------------------------------//
        [HttpGet("{id}/progress/{playerId}")]
        public async Task<IActionResult> GetProgramProgress(int id, int playerId)
        {
            if (id <= 0 || playerId <= 0)
                return BadRequest(new { error = "Invalid program ID or player ID." });

            var workouts = new List<object>();

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT
                        w.id AS workout_id,
                        w.title,
                        w.scheduled_date,
                        w.total_length_sec,
                        wpi.sort_order,
                        CASE
                            WHEN wc.workout_id IS NOT NULL AND wc.completed = TRUE THEN TRUE
                            ELSE FALSE
                        END AS completed,
                        wc.completed_at
                    FROM workout_program_items wpi
                    JOIN workouts w
                        ON w.id = wpi.workout_id
                    LEFT JOIN workout_completions wc
                        ON wc.workout_id = w.id
                        AND wc.player_id = @playerId
                    WHERE wpi.program_id = @programId
                    ORDER BY wpi.sort_order ASC, w.scheduled_date ASC;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@programId", id);
                command.Parameters.AddWithValue("@playerId", playerId);

                await using var reader = await command.ExecuteReaderAsync();

                int totalWorkouts = 0;
                int completedWorkouts = 0;

                while (await reader.ReadAsync())
                {
                    var completed = Convert.ToBoolean(reader["completed"]);

                    totalWorkouts++;
                    if (completed)
                        completedWorkouts++;

                    workouts.Add(new
                    {
                        WorkoutId = reader["workout_id"],
                        Title = reader["title"],
                        ScheduledDate = reader["scheduled_date"],
                        TotalLengthSec = reader["total_length_sec"],
                        SortOrder = reader["sort_order"],
                        Completed = completed,
                        CompletedAt = reader["completed_at"] == DBNull.Value ? null : reader["completed_at"]
                    });
                }

                bool programCompleted = totalWorkouts > 0 && completedWorkouts == totalWorkouts;

                return Ok(new
                {
                    ProgramId = id,
                    PlayerId = playerId,
                    TotalWorkouts = totalWorkouts,
                    CompletedWorkouts = completedWorkouts,
                    ProgramCompleted = programCompleted,
                    Workouts = workouts
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //     GET OVERALL COMPLETION FOR PROGRAM           //
        //--------------------------------------------------//
        [HttpGet("{id}/completed/{playerId}")]
        public async Task<IActionResult> GetProgramCompletionStatus(int id, int playerId)
        {
            if (id <= 0 || playerId <= 0)
                return BadRequest(new { error = "Invalid program ID or player ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT
                        COUNT(wpi.workout_id) AS total_workouts,
                        COUNT(wc.workout_id) AS completed_workouts
                    FROM workout_program_items wpi
                    LEFT JOIN workout_completions wc
                        ON wc.workout_id = wpi.workout_id
                        AND wc.player_id = @playerId
                        AND wc.completed = TRUE
                    WHERE wpi.program_id = @programId;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@programId", id);
                command.Parameters.AddWithValue("@playerId", playerId);

                await using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    int totalWorkouts = Convert.ToInt32(reader["total_workouts"]);
                    int completedWorkouts = Convert.ToInt32(reader["completed_workouts"]);
                    bool completed = totalWorkouts > 0 && totalWorkouts == completedWorkouts;

                    return Ok(new
                    {
                        ProgramId = id,
                        PlayerId = playerId,
                        TotalWorkouts = totalWorkouts,
                        CompletedWorkouts = completedWorkouts,
                        Completed = completed
                    });
                }

                return Ok(new
                {
                    ProgramId = id,
                    PlayerId = playerId,
                    TotalWorkouts = 0,
                    CompletedWorkouts = 0,
                    Completed = false
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
    //                    DTO CLASSES                    //
    //--------------------------------------------------//
    public class WorkoutProgramDto
    {
        public int? TeamId { get; set; }
        public int? CoachId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class WorkoutProgramUpdateDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class ProgramWorkoutItemDto
    {
        public int WorkoutId { get; set; }
        public int? SortOrder { get; set; }
    }

    public class ProgramWorkoutUpdateDto
    {
        public int SortOrder { get; set; }
    }
}