using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AudioAthleteApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SavedPromptsController : ControllerBase
    {
        private readonly string _connectionString;

        public SavedPromptsController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultDb");
        }

        // --------------------------------------------------//
        //        GET PROMPTS FOR A SAVED WORKOUT            //
        // --------------------------------------------------//
        [HttpGet("{savedWorkoutId}")]
        public async Task<IActionResult> GetSavedPrompts(int savedWorkoutId)
        {
            var results = new List<object>();
            int total = 0;

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, saved_workout_id, sort_order, block_length, instruction
                    FROM saved_workout_prompts
                    WHERE saved_workout_id = @id
                    ORDER BY sort_order ASC;
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", savedWorkoutId);

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var blockLen = Convert.ToInt32(reader["block_length"]);
                    total += blockLen;

                    results.Add(new
                    {
                        Id = reader["id"],
                        SavedWorkoutId = reader["saved_workout_id"],
                        SortOrder = reader["sort_order"],
                        BlockLength = blockLen,
                        Instruction = reader["instruction"]
                    });
                }

                return Ok(new { SavedWorkoutId = savedWorkoutId, TotalLengthSeconds = total, Prompts = results });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // --------------------------------------------------//
        //              ADD PROMPT TO SAVED                  //
        // --------------------------------------------------//
        [HttpPost]
        public async Task<IActionResult> AddSavedPrompt([FromBody] SavedPromptDto dto)
        {
            if (dto.SavedWorkoutId == null || dto.BlockLength <= 0 || string.IsNullOrWhiteSpace(dto.Instruction))
                return BadRequest(new { error = "SavedWorkoutId, BlockLength, Instruction are required." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var check = @"SELECT COUNT(*) FROM saved_workouts WHERE id = @id;";
                await using (var checkCmd = new MySqlCommand(check, connection))
                {
                    checkCmd.Parameters.AddWithValue("@id", dto.SavedWorkoutId);
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (exists == 0) return NotFound(new { error = "Saved workout not found." });
                }

                var insert = @"
                    INSERT INTO saved_workout_prompts (saved_workout_id, sort_order, block_length, instruction)
                    VALUES (@savedId, @sortOrder, @blockLength, @instruction);
                    SELECT LAST_INSERT_ID();
                ";

                await using var cmd = new MySqlCommand(insert, connection);
                cmd.Parameters.AddWithValue("@savedId", dto.SavedWorkoutId);
                cmd.Parameters.AddWithValue("@sortOrder", dto.SortOrder);
                cmd.Parameters.AddWithValue("@blockLength", dto.BlockLength);
                cmd.Parameters.AddWithValue("@instruction", dto.Instruction);

                var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return Ok(new { message = "Saved prompt added!", saved_prompt_id = id });
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return Conflict(new { error = "sortOrder already exists for this saved workout." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // --------------------------------------------------//
        //               DELETE SAVED PROMPT                 //
        // --------------------------------------------------//
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSavedPrompt(int id)
        {
            if (id <= 0) return BadRequest(new { error = "Invalid saved prompt ID." });

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"DELETE FROM saved_workout_prompts WHERE id = @id;";
                await using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@id", id);

                var rows = await cmd.ExecuteNonQueryAsync();
                if (rows > 0) return Ok(new { message = "Saved prompt deleted!" });
                return NotFound(new { error = "Saved prompt not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class SavedPromptDto
    {
        public int? SavedWorkoutId { get; set; }
        public int SortOrder { get; set; }
        public int BlockLength { get; set; }
        public string Instruction { get; set; } = string.Empty;
    }
}
