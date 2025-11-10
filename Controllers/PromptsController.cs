using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AudioAthleteApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PromptsController : ControllerBase
    {
        private readonly string _connectionString;

        public PromptsController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultDb");
        }

        //--------------------------------------------------//
        //             GET PROMPTS FOR A WORKOUT            //
        //--------------------------------------------------//
        [HttpGet("{workoutId}")]
        public async Task<IActionResult> GetPromptsByWorkout(int workoutId)
        {
            var results = new List<object>();
            int totalLength = 0;

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, workout_id, block_length, instruction
                    FROM workout_prompts
                    WHERE workout_id = @workoutId
                    ORDER BY id ASC;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@workoutId", workoutId);

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var blockLength = Convert.ToInt32(reader["block_length"]);
                    totalLength += blockLength;

                    results.Add(new
                    {
                        Id = reader["id"],
                        WorkoutId = reader["workout_id"],
                        BlockLength = blockLength,
                        Instruction = reader["instruction"]
                    });
                }

                return Ok(new
                {
                    WorkoutId = workoutId,
                    TotalLengthMinutes = totalLength,
                    Prompts = results
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                ADD PROMPT TO WORKOUT             //
        //--------------------------------------------------//
        [HttpPost]
        public async Task<IActionResult> AddPrompt([FromBody] WorkoutPromptDto newPrompt)
        {
            if (newPrompt.WorkoutId == null ||
                newPrompt.BlockLength <= 0 ||
                string.IsNullOrWhiteSpace(newPrompt.Instruction))
            {
                return BadRequest(new { error = "WorkoutId, BlockLength, and Instruction are required." });
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // verify workout exists
                var checkWorkoutQuery = @"SELECT COUNT(*) FROM workouts WHERE id = @id;";
                await using (var checkCmd = new MySqlCommand(checkWorkoutQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@id", newPrompt.WorkoutId);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (exists == 0)
                        return NotFound(new { error = "Workout not found." });
                }

                var insertQuery = @"
                    INSERT INTO workout_prompts (workout_id, block_length, instruction)
                    VALUES (@workoutId, @blockLength, @instruction);
                    SELECT LAST_INSERT_ID();
                ";

                await using var insertCmd = new MySqlCommand(insertQuery, connection);
                insertCmd.Parameters.AddWithValue("@workoutId", newPrompt.WorkoutId);
                insertCmd.Parameters.AddWithValue("@blockLength", newPrompt.BlockLength);
                insertCmd.Parameters.AddWithValue("@instruction", newPrompt.Instruction);

                var promptId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync());

                return Ok(new
                {
                    message = "Prompt added successfully!",
                    prompt_id = promptId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //             DELETE A SPECIFIC PROMPT             //
        //--------------------------------------------------//
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePrompt(int id)
        {
            if (id <= 0)
                return BadRequest(new { error = "Invalid prompt ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"DELETE FROM workout_prompts WHERE id = @id;";
                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);

                var rows = await command.ExecuteNonQueryAsync();

                if (rows > 0)
                    return Ok(new { message = "Prompt deleted successfully!" });
                else
                    return NotFound(new { error = "Prompt not found." });
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
    public class WorkoutPromptDto
    {
        public int? WorkoutId { get; set; }
        public int BlockLength { get; set; }
        public string Instruction { get; set; } = string.Empty;
    }
}
