using MySql.Data.MySqlClient;

namespace AivenApi.Helpers
{
    public static class DbHelper
    {
        // Replace these with your actual environment variables or hardcoded values for now
        private static string Host = "mysql-36a9fcbd-audio-athlete.g.aivencloud.com";
        private static string Database = "Audio-Athlete";
        private static string User = "avnadmin";
        private static string Password = "AVNS_EBN9--0Y8LWbedqtE-n";
        private static uint Port = 19131;

        public static MySqlConnection GetConnection()
        {
            string connStr = $"Server={Host};Database={Database};User={User};Password={Password};Port={Port};SslMode=Required;";
            var conn = new MySqlConnection(connStr);
            conn.Open();
            return conn;
        }
    }
}

