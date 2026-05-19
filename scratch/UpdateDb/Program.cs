using System;
using Microsoft.Data.Sqlite;

namespace UpdateDb
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                using var connection = new SqliteConnection("Data Source=C:\\Users\\adm-kh\\source\\repos\\EZKPM\\ezkpm_vault.db");
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE UserProfiles ADD COLUMN LastLoginAt TEXT;";
                command.ExecuteNonQuery();

                Console.WriteLine("Successfully added LastLoginAt to UserProfiles.");
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("duplicate column name"))
                {
                    Console.WriteLine("Column already exists.");
                }
                else
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }
    }
}
