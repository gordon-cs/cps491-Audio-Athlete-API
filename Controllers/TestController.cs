using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AudioAthleteApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly string _connectionString;

        public TestController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultDb") ?? throw new InvalidOperationException("DefaultDb connection string missing");
        }

        //--------------------------------------------------//
        //                 GET TEST DATA                    //
        //--------------------------------------------------//
        [HttpGet]
        public async Task<IActionResult> GetTest()
        {
            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"SELECT 1 AS test_value;";
                await using var command = new MySqlCommand(query, connection);
                var result = await command.ExecuteScalarAsync();

                return Ok(new { test = result });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //                 POST TEST DATA                   //
        //--------------------------------------------------//
        [HttpPost]
        public IActionResult PostTest([FromBody] TestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Message))
                return BadRequest(new { error = "Message is required." });

            return Ok(new { message = $"Received: {dto.Message}" });
        }
    }

    //--------------------------------------------------//
    //                    DTO CLASS                     //
    //--------------------------------------------------//
    public class TestDto
    {
        public string Message { get; set; } = string.Empty;
    }
}
