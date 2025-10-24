using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AivenApi.Controllers
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
                Console.WriteLine(ex.ToString());
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

                var query = @"
                    INSERT INTO 'users' ('name', 'password', 'user_type') 
                    VALUES (@name, @password, @userType);
                ";

                await using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@name", newUser.Name);
                command.Parameters.AddWithValue("@password", newUser.Password);
                command.Parameters.AddWithValue("@userType", newUser.UserType);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Ok(new { message = "User added successfully!" });
                }
                else
                {
                    return StatusCode(500, new { error = "Failed to add user." });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
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

            var query = @"DELETE FROM 'users' 
                        WHERE 'id' = @id;
            ";
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
            Console.WriteLine(ex.ToString());
            return StatusCode(500, new { error = ex.Message });
        }
    }

}
    // DTO class outside of the controller
    public class UserDto
    {
        public string Name { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string UserType { get; set; } = string.Empty;
        public string TeamId { get; set; } = string.Empty;

    }
}
