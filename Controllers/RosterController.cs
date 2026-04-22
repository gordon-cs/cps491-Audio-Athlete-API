using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Isopoh.Cryptography.Argon2;
using System.Text;

namespace AudioAthleteApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RosterController : ControllerBase
    {
        private readonly string _connectionString;

        public RosterController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultDb");
        }

        //--------------------------------------------------//
        //               ADD PLAYER TO ROSTER               //
        //--------------------------------------------------//
        [HttpPost("{teamId}/add")]
        public async Task<IActionResult> AddPlayer(int teamId, [FromBody] RosterPlayerDto player)
        {
            if (string.IsNullOrWhiteSpace(player.Name) ||
                string.IsNullOrWhiteSpace(player.Username) ||
                string.IsNullOrWhiteSpace(player.Password) ||
                string.IsNullOrWhiteSpace(player.Position))
            {
                return BadRequest(new { error = "Name, Username, Password, and Position are required." });
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();

                var teamCheckQuery = @"SELECT COUNT(*) FROM teams WHERE id = @teamId;";
                await using (var cmd = new MySqlCommand(teamCheckQuery, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@teamId", teamId);
                    var exists = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    if (exists == 0)
                    {
                        await transaction.RollbackAsync();
                        return NotFound(new { error = "Team not found." });
                    }
                }

                int playerId;
                var insertPlayer = @"
                    INSERT INTO users (name, username, password, user_type, position, team_id, email)
                    VALUES (@name, @username, @password, 'player', @position, @teamId, @email);
                    SELECT LAST_INSERT_ID();
                ";

                await using (var cmd = new MySqlCommand(insertPlayer, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@name", player.Name);
                    cmd.Parameters.AddWithValue("@username", player.Username);
                    cmd.Parameters.AddWithValue("@password", HashPassword(player.Password));
                    cmd.Parameters.AddWithValue("@position", player.Position);
                    cmd.Parameters.AddWithValue("@teamId", teamId);
                    cmd.Parameters.AddWithValue("@email", player.Email ?? (object)DBNull.Value);
                    
                    playerId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                await transaction.CommitAsync();

                return Ok(new
                {
                    message = "Player added to roster!",
                    player_id = playerId,
                    team_id = teamId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //               EDIT PLAYER DETAILS                //
        //--------------------------------------------------//
        [HttpPut("{playerId}")]
        public async Task<IActionResult> EditPlayer(int playerId, [FromBody] RosterPlayerUpdateDto update)
        {
            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE users
                    SET 
                        name = COALESCE(@name, name),
                        username = COALESCE(@username, username),
                        password = COALESCE(@password, password),
                        position = COALESCE(@position, position)
                    WHERE id = @playerId AND user_type = 'player';
                ";

                await using var cmd = new MySqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@playerId", playerId);
                cmd.Parameters.AddWithValue("@name", update.Name ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@username", update.Username ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@password",
                    update.Password != null
                        ? HashPassword(update.Password)
                        : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@position", update.Position ?? (object)DBNull.Value);

                var rows = await cmd.ExecuteNonQueryAsync();

                if (rows == 0)
                    return NotFound(new { error = "Player not found or not a player." });

                return Ok(new { message = "Player updated successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //--------------------------------------------------//
        //              DELETE PLAYER FROM ROSTER           //
        //--------------------------------------------------//
        [HttpDelete("{playerId}")]
        public async Task<IActionResult> DeletePlayer(int playerId)
        {
            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var deleteUser = @"DELETE FROM users WHERE id = @playerId AND user_type = 'player';";

                await using var cmd = new MySqlCommand(deleteUser, connection);
                cmd.Parameters.AddWithValue("@playerId", playerId);

                var rows = await cmd.ExecuteNonQueryAsync();

                if (rows == 0)
                    return NotFound(new { error = "Player not found." });

                return Ok(new { message = "Player deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private string HashPassword(string password)
        {
            byte[] salt = new byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(salt);

            var config = new Argon2Config
            {
                Type = Argon2Type.DataIndependentAddressing,
                TimeCost = 4,
                MemoryCost = 1024 * 64,
                Lanes = 4,
                Threads = 4,
                Salt = salt,
                Password = Encoding.UTF8.GetBytes(password)
            };

            using var argon2 = new Argon2(config);
            var hash = argon2.Hash();

            return config.EncodeString(hash.Buffer);
        }
    }

    public class RosterPlayerDto
    {
        public string Name { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    public class RosterPlayerUpdateDto
    {
        public string? Name { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Position { get; set; }
        
    }
}
