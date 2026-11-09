using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);

// Database configuration
var dbHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";
var dbName = Environment.GetEnvironmentVariable("DB_NAME") ?? "notesdb";
var dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? "notesuser";
var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "notespassword";

var connectionBuilder = new MySqlConnectionStringBuilder
{
    Server = dbHost,
    Port = uint.Parse(dbPort),
    Database = dbName,
    UserID = dbUser,
    Password = dbPassword
};

string connectionString = connectionBuilder.ConnectionString;

var app = builder.Build();

// Serve the existing HTML frontend from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize MariaDB and create the notes table
bool connected = false;

for (int i = 1; i <= 10; i++)
{
    try
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        string sql = """
            CREATE TABLE IF NOT EXISTS notes (
                id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                title TEXT NOT NULL,
                content TEXT
            );
            """;

        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();

        Console.WriteLine("Connected to MariaDB");
        connected = true;
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Waiting for database ({i}/10)... {ex.Message}");
        await Task.Delay(2000);
    }
}

if (!connected)
{
    Console.WriteLine("Could not connect to MariaDB");
    throw new Exception("Database connection failed");
}

// Health check
app.MapGet("/health", async Task<IResult> () =>
{
    try
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        return Results.Ok(new
        {
            status = "Healthy",
            database = "Connected"
        });
    }
    catch
    {
        return Results.Json(
            new { status = "Unhealthy", database = "Unavailable" },
            statusCode: 503
        );
    }
});

// CREATE
// POST /notes
app.MapPost("/notes", async Task<IResult> (NoteRequest request) =>
{
    try
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        string sql = """
            INSERT INTO notes (title, content)
            VALUES (@title, @content);
            """;

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@title", (object?)request.Title ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@content", (object?)request.Content ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();

        int newId = checked((int)command.LastInsertedId);

        var note = await GetNoteById(connection, newId);

        return Results.Json(note, statusCode: 201);
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { error = ex.Message },
            statusCode: 500
        );
    }
});

// READ ALL
// GET /notes
app.MapGet("/notes", async Task<IResult> () =>
{
    try
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        string sql = "SELECT * FROM notes ORDER BY id";

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var notes = new List<Note>();

        while (await reader.ReadAsync())
        {
            notes.Add(ReadNote(reader));
        }

        return Results.Ok(notes);
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { error = ex.Message },
            statusCode: 500
        );
    }
});

// READ ONE
// GET /notes/{id}
app.MapGet("/notes/{id:int}", async Task<IResult> (int id) =>
{
    try
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        var note = await GetNoteById(connection, id);

        if (note == null)
        {
            return Results.NotFound(new { error = "Not found" });
        }

        return Results.Ok(note);
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { error = ex.Message },
            statusCode: 500
        );
    }
});

// UPDATE
// PUT /notes/{id}
app.MapPut("/notes/{id:int}", async Task<IResult> (int id, NoteRequest request) =>
{
    try
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        string sql = """
            UPDATE notes
            SET title = @title, content = @content
            WHERE id = @id;
            """;

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@title", (object?)request.Title ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@content", (object?)request.Content ?? DBNull.Value);

        command.Parameters.AddWithValue("@id", id);

        await command.ExecuteNonQueryAsync();

        // Check whether the note exists after updating
        var updatedNote = await GetNoteById(connection, id);

        if (updatedNote == null)
        {
            return Results.NotFound(new { error = "Not found" });
        }

        return Results.Ok(updatedNote);
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { error = ex.Message },
            statusCode: 500
        );
    }
});

// DELETE
// DELETE /notes/{id}
app.MapDelete("/notes/{id:int}", async Task<IResult> (int id) =>
{
    try
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        string sql = "DELETE FROM notes WHERE id = @id";

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@id", id);

        await command.ExecuteNonQueryAsync();

        return Results.Ok(new { message = "Deleted" });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { error = ex.Message },
            statusCode: 500
        );
    }
});

// Start application
app.Run();


// Helper method: Get a note using its ID
static async Task<Note?> GetNoteById(MySqlConnection connection, int id)
{
    string sql = "SELECT * FROM notes WHERE id = @id";

    await using var command = new MySqlCommand(sql, connection);

    command.Parameters.AddWithValue("@id", id);

    await using var reader = await command.ExecuteReaderAsync();

    if (await reader.ReadAsync())
    {
        return ReadNote(reader);
    }

    return null;
}


// Helper method: Convert database row into a C# Note object
static Note ReadNote(MySqlDataReader reader)
{
    return new Note
    {
        Id = reader.GetInt32(reader.GetOrdinal("id")),
        Title = reader.GetString(reader.GetOrdinal("title")),
        Content = reader.IsDBNull(reader.GetOrdinal("content"))
            ? null
            : reader.GetString(reader.GetOrdinal("content"))
    };
}


// Note model
public class Note
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Content { get; set; }
}


// Request model
public class NoteRequest
{
    public string? Title { get; set; }
    public string? Content { get; set; }
}